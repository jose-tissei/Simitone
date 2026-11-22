// FXAA-style anti-aliasing for the world render target, drawn at the world-to-screen blit (before the UI).
// Luma edge detection followed by a blur along the edge direction (the classic approach from the public FXAA
// description), written for this project. Same vertex layout and parameters style as the game's own SSAA.fx.

float2 FxaaTexel;      // 1 / target size

texture tex : Diffuse;
sampler texSampler = sampler_state {
	texture = <tex>;
	AddressU = CLAMP; AddressV = CLAMP; AddressW = CLAMP;
	MIPFILTER = POINT; MINFILTER = LINEAR; MAGFILTER = LINEAR;
};

struct VertexShaderInput
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_Position0;
    float2 Coord : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    output.Position = input.Position;
    output.Coord = input.Coord;
	output.Coord.y = 1 - output.Coord.y;
    return output;
}

float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

float4 Tap(float2 uv) { return tex2D(texSampler, uv); }

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
	float2 uv = input.Coord;
	float2 px = FxaaTexel;

	float4 cM = Tap(uv);
	float4 cNW = Tap(uv + float2(-1, -1) * px);
	float4 cNE = Tap(uv + float2(1, -1) * px);
	float4 cSW = Tap(uv + float2(-1, 1) * px);
	float4 cSE = Tap(uv + float2(1, 1) * px);

	float lM = Luma(cM.rgb), lNW = Luma(cNW.rgb), lNE = Luma(cNE.rgb), lSW = Luma(cSW.rgb), lSE = Luma(cSE.rgb);
	float lMin = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
	float lMax = max(lM, max(max(lNW, lNE), max(lSW, lSE)));

	// flat area: leave it alone (keeps the picture and the text-free parts bit-identical)
	if (lMax - lMin < max(0.0833, lMax * 0.166)) return cM;

	float2 dir;
	dir.x = -((lNW + lNE) - (lSW + lSE));
	dir.y = ((lNW + lSW) - (lNE + lSE));
	float dirReduce = max((lNW + lNE + lSW + lSE) * 0.03125, 1.0 / 128.0);
	float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
	dir = clamp(dir * rcpDirMin, -8.0, 8.0) * px;

	float4 a = 0.5 * (Tap(uv + dir * (1.0 / 3.0 - 0.5)) + Tap(uv + dir * (2.0 / 3.0 - 0.5)));
	float4 b = a * 0.5 + 0.25 * (Tap(uv + dir * -0.5) + Tap(uv + dir * 0.5));
	float lB = Luma(b.rgb);
	return (lB < lMin || lB > lMax) ? a : b;
}

technique DrawFxaa
{
    pass MainPass
    {
#if SM4
        VertexShader = compile vs_4_0_level_9_1 VertexShaderFunction();
        PixelShader = compile ps_4_0_level_9_1 PixelShaderFunction();
#else
        VertexShader = compile vs_3_0 VertexShaderFunction();
        PixelShader = compile ps_3_0 PixelShaderFunction();
#endif;
    }
}
