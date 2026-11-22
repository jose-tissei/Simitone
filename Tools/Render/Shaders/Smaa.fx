// SMAA 1x for the world render target: three passes (edge detection, blending weights, neighbourhood blending) built on
// the reference implementation SMAA.hlsl (MIT, see SMAA-LICENSE.txt). This file only supplies the vertex layout, the samplers
// and the techniques, in the style of the game's own SSAA.fx.

float4 SmaaMetrics;    // (1/width, 1/height, width, height) of the world render target

#define SMAA_RT_METRICS SmaaMetrics
#define SMAA_HLSL_3 1
#define SMAA_PRESET_HIGH 1
#include "SMAA.hlsl"

texture colorTexture;
sampler colorTex = sampler_state { texture = <colorTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };
sampler colorPointTex = sampler_state { texture = <colorTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };
texture edgesTexture;
sampler edgesTex = sampler_state { texture = <edgesTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };
texture blendTexture;
sampler blendTex = sampler_state { texture = <blendTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR; };
texture areaTexture;
sampler areaTex = sampler_state { texture = <areaTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };
texture searchTexture;
sampler searchTex = sampler_state { texture = <searchTexture>; AddressU = CLAMP; AddressV = CLAMP; MIPFILTER = POINT; MINFILTER = POINT; MAGFILTER = POINT; };

struct VSIn
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
};

struct EdgeVSOut
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
    float4 Offset0 : TEXCOORD1;
    float4 Offset1 : TEXCOORD2;
    float4 Offset2 : TEXCOORD3;
};

struct WeightVSOut
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
    float4 Offset0 : TEXCOORD1;
    float4 Offset1 : TEXCOORD2;
    float4 Offset2 : TEXCOORD3;
    float2 PixCoord : TEXCOORD4;
};

struct BlendVSOut
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
    float4 Offset : TEXCOORD1;
};

// ---- pass 1: luma edge detection -> edgesTexture
EdgeVSOut EdgeVS(VSIn input)
{
    EdgeVSOut o = (EdgeVSOut)0;
    o.Position = input.Position;
    o.Coord = float2(input.Coord.x, 1 - input.Coord.y);
    float4 offs[3];
    SMAAEdgeDetectionVS(o.Coord, offs);
    o.Offset0 = offs[0]; o.Offset1 = offs[1]; o.Offset2 = offs[2];
    return o;
}

float4 EdgePS(EdgeVSOut i) : COLOR0
{
    float4 offs[3] = { i.Offset0, i.Offset1, i.Offset2 };
    return float4(SMAALumaEdgeDetectionPS(i.Coord, offs, colorPointTex), 0, 0);
}

// ---- pass 2: blending weights -> blendTexture
WeightVSOut WeightVS(VSIn input)
{
    WeightVSOut o = (WeightVSOut)0;
    o.Position = input.Position;
    o.Coord = float2(input.Coord.x, 1 - input.Coord.y);
    float4 offs[3];
    SMAABlendingWeightCalculationVS(o.Coord, o.PixCoord, offs);
    o.Offset0 = offs[0]; o.Offset1 = offs[1]; o.Offset2 = offs[2];
    return o;
}

float4 WeightPS(WeightVSOut i) : COLOR0
{
    float4 offs[3] = { i.Offset0, i.Offset1, i.Offset2 };
    return SMAABlendingWeightCalculationPS(i.Coord, i.PixCoord, offs, edgesTex, areaTex, searchTex, float4(0, 0, 0, 0));
}

// ---- pass 3: neighbourhood blending -> the screen
BlendVSOut BlendVS(VSIn input)
{
    BlendVSOut o = (BlendVSOut)0;
    o.Position = input.Position;
    o.Coord = float2(input.Coord.x, 1 - input.Coord.y);
    SMAANeighborhoodBlendingVS(o.Coord, o.Offset);
    return o;
}

float4 BlendPS(BlendVSOut i) : COLOR0
{
    return SMAANeighborhoodBlendingPS(i.Coord, i.Offset, colorTex, blendTex);
}

technique SmaaEdges
{
    pass P { VertexShader = compile vs_4_0 EdgeVS(); PixelShader = compile ps_4_0 EdgePS(); }
}

technique SmaaWeights
{
    pass P { VertexShader = compile vs_4_0 WeightVS(); PixelShader = compile ps_4_0 WeightPS(); }
}

technique SmaaBlend
{
    pass P { VertexShader = compile vs_4_0 BlendVS(); PixelShader = compile ps_4_0 BlendPS(); }
}
