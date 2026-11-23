// Radiance cascades over the lot's plan-view light map (2D, pixel shaders only; the engine's effect model has no compute).
//
// The scene is an atlas laid out like the light map (3 floors wide, 2 high) at a few pixels per tile: rgb = the light the engine
// has already put on the floor (light acts as a glowing medium), a = 1 where a wall blocks light. Cascade i holds probes spaced
// 2^(i+1) pixels apart; each probe stores 4^(i+1) ray directions packed in its block of the texture, and each ray covers a
// longer, farther interval than the one below. Cascades are merged from the farthest down to cascade 0 (a ray that is not
// blocked adds what the next cascade saw in the same direction), so one cheap pass per cascade gives every pixel the light
// that reaches it from everywhere around, with walls casting soft shadows. The combine pass adds that to the light map.
// Method: Alexander Sannikov, "Radiance Cascades" (2023); this code is written for this project.

float2 SceneSize;      // pixels of the scene atlas
float FloorPx;         // pixels of one floor (multiple of 16)
float Tiles;           // tiles per floor side
float PxPerTile;       // scene pixels per tile
float Block;           // cascade block size, 2 << cascade
float Start;           // ray interval start, in scene pixels
float End;             // ray interval end
float Reach;           // total reach of all cascades, in pixels (normalises the light a ray collects)
float IsTop;           // 1 for the last cascade (nothing above it to merge)
float Gain;            // how much of the gathered light is added
float2 LightTexel;     // 1 / size of the light map
float Debug;           // 1 = show the walls, 2 = show the gathered light alone (amplified)

texture lightTexture;
sampler lightTex = sampler_state { texture = <lightTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };
texture wallTexture;
sampler wallTex = sampler_state { texture = <wallTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };
texture sceneTexture;
sampler sceneTex = sampler_state { texture = <sceneTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };
texture upperTexture;
sampler upperTex = sampler_state { texture = <upperTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };
texture cascadeTexture;
sampler cascadeTex = sampler_state { texture = <cascadeTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };

struct VSIn { float4 Position : SV_Position0; float2 Coord : TEXCOORD0; };
struct VSOut { float4 Position : SV_Position0; float2 Coord : TEXCOORD0; };

VSOut VS(VSIn input)
{
    VSOut o = (VSOut)0;
    o.Position = input.Position;
    o.Coord = float2(input.Coord.x, 1 - input.Coord.y);
    return o;
}

// scene pixel -> light map uv, and back
float2 SceneToLight(float2 spx)
{
    float2 fl = floor(spx / FloorPx);
    float2 local = (spx - fl * FloorPx) / (PxPerTile * Tiles);
    return (fl + local) / float2(3, 2);
}

float2 LightToScene(float2 uv)
{
    float2 a = uv * float2(3, 2);
    float2 fl = floor(a);
    return fl * FloorPx + (a - fl) * (PxPerTile * Tiles);
}

// pass 1: scene = the lamps (small glowing discs, so the sun and moon, which fill the light map, are never sources) + walls
// everything outside a floor counts as wall
#define MAX_LIGHTS 48
float LightCount;
float4 LightPosR[MAX_LIGHTS];    // x, y in scene pixels, radius in scene pixels
float4 LightColor[MAX_LIGHTS];   // rgb (already multiplied by the light's intensity)
float EmitScale;

float4 ScenePS(VSOut i) : COLOR0
{
    float2 spx = floor(i.Coord * SceneSize) + 0.5;
    float2 fl = floor(spx / FloorPx);
    float2 local = spx - fl * FloorPx;
    float wall = tex2D(wallTex, i.Coord).a;
    if (local.x >= Tiles * PxPerTile || local.y >= Tiles * PxPerTile) return float4(0, 0, 0, 1);
    float3 e = 0;
    [loop]
    for (int k = 0; k < MAX_LIGHTS; k++)
    {
        if (k >= LightCount) break;
        float4 a = LightPosR[k];
        float r = length(spx - a.xy) / a.z;
        if (r < 1) e += LightColor[k].rgb * ((1 - r) * (1 - r));
    }
    return float4(e * EmitScale, wall);
}

// pass 2 (once per cascade): every pixel is one ray of one probe
float4 CascadePS(VSOut i) : COLOR0
{
    float2 px = floor(i.Coord * SceneSize);
    float2 blk = floor(px / Block);
    float2 loc = px - blk * Block;
    float N = Block * Block;
    float d = loc.y * Block + loc.x;
    float ang = (d + 0.5) / N * 6.2831853;
    float2 dir = float2(cos(ang), sin(ang));
    float2 origin = blk * Block + Block * 0.5;
    float2 fl = floor(origin / FloorPx);
    float2 lo = fl * FloorPx;
    float2 hi = lo + FloorPx;

    float3 rad = 0;
    float trans = 1;
    float step = 0.75;
    [loop]
    for (float t = Start + step * 0.5; t < End; t += step)
    {
        float2 p = origin + dir * t;
        if (p.x < lo.x || p.y < lo.y || p.x >= hi.x || p.y >= hi.y) { trans = 0; break; }
        float4 s = tex2Dlod(sceneTex, float4(p / SceneSize, 0, 0));
        float taper = saturate(1 - t / Reach);     // fade to nothing at the total reach, so the glow has no visible rim
        rad += s.rgb * (step / Reach) * (taper * taper);
        if (s.a > 0.5) { trans = 0; break; }
    }

    if (trans > 0 && IsTop < 0.5)
    {
        // merge: what the next cascade saw in this direction, from the four probes around this one
        float b2 = Block * 2;
        float2 pf = origin / b2 - 0.5;
        float2 base = floor(pf);
        float2 fr = pf - base;
        float2 gridMax = SceneSize / b2 - 1;
        float3 up = 0;
        [unroll]
        for (int c = 0; c < 4; c++)
        {
            float2 o = float2(c & 1, c >> 1);
            float2 g = clamp(base + o, 0, gridMax);
            float w = (o.x > 0 ? fr.x : 1 - fr.x) * (o.y > 0 ? fr.y : 1 - fr.y);
            float3 acc = 0;
            [unroll]
            for (int k = 0; k < 4; k++)
            {
                float d2 = d * 4 + k;
                float2 q = g * b2 + float2(fmod(d2, b2), floor(d2 / b2)) + 0.5;
                acc += tex2Dlod(upperTex, float4(q / SceneSize, 0, 0)).rgb;
            }
            up += acc * 0.25 * w;
        }
        rad += up;
    }
    return float4(rad, 1);
}

// pass 3: add the gathered light to the light map where it is not fully lit
float4 CombinePS(VSOut i) : COLOR0
{
    float4 L = tex2D(lightTex, i.Coord);
    float2 spx = LightToScene(i.Coord);
    float2 pf = spx / 2 - 0.5;
    float2 base = floor(pf);
    float2 fr = pf - base;
    float2 gridMax = SceneSize / 2 - 1;
    float3 B = 0;
    [unroll]
    for (int c = 0; c < 4; c++)
    {
        float2 o = float2(c & 1, c >> 1);
        float2 g = clamp(base + o, 0, gridMax);
        float w = (o.x > 0 ? fr.x : 1 - fr.x) * (o.y > 0 ? fr.y : 1 - fr.y);
        float3 acc = 0;
        [unroll]
        for (int k = 0; k < 4; k++)
            acc += tex2Dlod(cascadeTex, float4((g * 2 + float2(k & 1, k >> 1) + 0.5) / SceneSize, 0, 0)).rgb;
        B += acc * 0.25 * w;
    }
    if (Debug > 1.5) return float4(saturate(B * 4), 1);
    if (Debug > 0.5) { float wl = 0; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) wl = max(wl, tex2Dlod(sceneTex, float4((spx + float2(dx, dy) + 0.5) / SceneSize, 0, 0)).a); return float4(wl, L.rgb.g * 0.5, 0, 1); }
    float luma = dot(L.rgb, float3(0.299, 0.587, 0.114));
    float3 add = B * Gain * saturate(1 - luma);
    float a = L.a + dot(add, float3(0.3333, 0.3333, 0.3333));
    return float4(L.rgb + add, a);
}

technique Scene { pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 ScenePS(); } }
technique Cascade { pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 CascadePS(); } }
technique Combine { pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 CombinePS(); } }
