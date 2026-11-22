// Bounce light for the lot's light map (the plan-view lighting the engine bakes per floor: rgb = light intensity, a = the same
// with shadows). Pass 1 gathers light from the surroundings of every texel (a spiral of taps, nearer taps count more);
// pass 2 adds a share of it to the texels that are not fully lit yet. So a shadowed corner next to a lit area becomes softly lit,
// and coloured light spreads a little, like indirect light. Same vertex layout as the game's own full-screen effects.

float2 Texel;          // 1 / size of the light map
float PixRadius;       // reach of the gather, in light-map texels
float Gain;            // how much of the gathered light is added

texture lightTexture;
sampler lightTex = sampler_state { texture = <lightTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };
texture bounceTexture;
sampler bounceTex = sampler_state { texture = <bounceTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };

struct VSIn { float4 Position : SV_Position0; float2 Coord : TEXCOORD0; };
struct VSOut { float4 Position : SV_Position0; float2 Coord : TEXCOORD0; };

VSOut VS(VSIn input)
{
    VSOut o = (VSOut)0;
    o.Position = input.Position;
    o.Coord = float2(input.Coord.x, 1 - input.Coord.y);
    return o;
}

#define TAPS 32

float4 GatherPS(VSOut i) : COLOR0
{
    float3 sum = 0;
    float wsum = 0;
    [unroll]
    for (int k = 0; k < TAPS; k++)
    {
        float r = sqrt((k + 0.5) / TAPS);              // even coverage of the disc
        float a = k * 2.39996323;                       // golden angle
        float2 off = float2(cos(a), sin(a)) * r * PixRadius * Texel;
        float w = (1 - r) * (1 - r) + 0.02;
        sum += tex2D(lightTex, i.Coord + off).rgb * w;
        wsum += w;
    }
    return float4(sum / wsum, 1);
}

float4 CombinePS(VSOut i) : COLOR0
{
    float4 L = tex2D(lightTex, i.Coord);
    float3 B = tex2D(bounceTex, i.Coord).rgb;
    float luma = dot(L.rgb, float3(0.299, 0.587, 0.114));
    float3 add = B * Gain * saturate(1 - luma);        // only fills in what is not already lit
    float a = L.a + dot(add, float3(0.3333, 0.3333, 0.3333));   // the floor shading compares a to the average, so lift both
    return float4(L.rgb + add, a);
}

technique Gather
{
    pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 GatherPS(); }
}

technique Combine
{
    pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 CombinePS(); }
}
