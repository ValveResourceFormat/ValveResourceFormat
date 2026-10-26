#version 460

// D_BLUR_PASS = 0 -> 2x2 box downsample into the next mip
// D_BLUR_PASS = 1 -> horizontal gaussian blur of one mip
// D_BLUR_PASS = 2 -> vertical gaussian blur of one mip
// D_BLUR_PASS = 3 -> upsample of the mip below, tinted, blended over the tinted target mip
// D_BLUR_PASS = 4 -> final upsample of the first two mips, both tinted, into the bloom output

#define D_BLUR_PASS 0
#define D_TENT_FILTER 0

// xy is one over the source texture size, zw the last texel centre the blur may sample
uniform vec4 g_vTexelSize;
uniform float g_nSourceMip;

layout(binding = 0) uniform sampler2D g_tSource;

#if D_BLUR_PASS == 3
    // Dual source blending scales the target mip by the second output and adds the first
    layout(location = 0, index = 0) out vec4 outputColor;
    layout(location = 0, index = 1) out vec4 outputTargetWeight;
    uniform vec3 g_vCurrMipBlurTint = vec3(1.0);
#else
    layout(location = 0) out vec4 outputColor;
#endif

#if D_BLUR_PASS == 1 || D_BLUR_PASS == 2
    const float pixelOffsets[4] = { -2.196197f, -0.430063f, 1.0f, 2.196197f };
    const float gaussianWeights[4] = { 0.121596f, 0.529213f, 0.227595f, 0.121596f };
#elif D_BLUR_PASS >= 3
    // xy scales target pixels to source uv, zw clamps the uv to the last source texel centre
    uniform vec4 g_vCombineUVScaleAndClamp;
    uniform vec3 g_vPrevMipBlurTint = vec3(1.0);
    uniform float g_flTentFilterRadius = 1.0;
#endif

#if D_BLUR_PASS == 4
    uniform vec3 g_vCurrMipBlurTint = vec3(1.0);
    // zw clamps the uv to the last texel centre of the second source mip
    uniform vec4 g_vCombineUVScaleAndClamp2;
    uniform float g_nSource2Mip;
#endif

vec3 SampleSource(vec2 uv)
{
    return textureLod(g_tSource, uv, g_nSourceMip).rgb;
}

void main()
{
#if D_BLUR_PASS == 0
    vec3 color = SampleSource(gl_FragCoord.xy * 2.0 * g_vTexelSize.xy);
    outputColor = vec4(clamp(color, 0.0, 65504.0), 1.0);
#elif D_BLUR_PASS == 1 || D_BLUR_PASS == 2
    vec3 blurOutput = vec3(0.0);

    for (int i = 0; i < 4; i++)
    {
    #if D_BLUR_PASS == 1
        vec2 offset = vec2(pixelOffsets[i], 0.0);
    #else
        vec2 offset = vec2(0.0, pixelOffsets[i]);
    #endif

        vec2 samplePos = min(gl_FragCoord.xy + offset, g_vTexelSize.zw);
        blurOutput += SampleSource(samplePos * g_vTexelSize.xy) * gaussianWeights[i];
    }

    outputColor = vec4(blurOutput, 1.0);
#elif D_BLUR_PASS >= 3
    vec2 uvScale = g_vCombineUVScaleAndClamp.xy;
    vec2 uvClamp = g_vCombineUVScaleAndClamp.zw;

    #if D_TENT_FILTER == 1
        vec2 filterRadius = vec2(g_flTentFilterRadius);
    #else
        vec2 filterRadius = vec2(1.0);
    #endif

    vec2 minUV = min(uvScale * (gl_FragCoord.xy - filterRadius), uvClamp);
    vec2 maxUV = min(uvScale * (gl_FragCoord.xy + filterRadius), uvClamp);

    #if D_TENT_FILTER == 1
        vec2 centerUV = min(uvScale * gl_FragCoord.xy, uvClamp);

        vec3 upsampled = SampleSource(centerUV) * 0.25;
        upsampled += SampleSource(minUV) * 0.0625;
        upsampled += SampleSource(vec2(minUV.x, maxUV.y)) * 0.0625;
        upsampled += SampleSource(maxUV) * 0.0625;
        upsampled += SampleSource(vec2(maxUV.x, minUV.y)) * 0.0625;
        upsampled += SampleSource(vec2(centerUV.x, minUV.y)) * 0.125;
        upsampled += SampleSource(vec2(centerUV.x, maxUV.y)) * 0.125;
        upsampled += SampleSource(vec2(minUV.x, centerUV.y)) * 0.125;
        upsampled += SampleSource(vec2(maxUV.x, centerUV.y)) * 0.125;
    #else
        vec3 upsampled = SampleSource(minUV);
        upsampled += SampleSource(vec2(minUV.x, maxUV.y));
        upsampled += SampleSource(maxUV);
        upsampled += SampleSource(vec2(maxUV.x, minUV.y));
        upsampled *= 0.25;
    #endif

    #if D_BLUR_PASS == 3
        outputColor = vec4(upsampled * g_vPrevMipBlurTint, 1.0);
        outputTargetWeight = vec4(g_vCurrMipBlurTint, 1.0);
    #else
        vec2 source2UV = min(g_vCombineUVScaleAndClamp.xy * gl_FragCoord.xy, g_vCombineUVScaleAndClamp2.zw);
        vec3 currentMip = textureLod(g_tSource, source2UV, g_nSource2Mip).rgb;
        outputColor = vec4(upsampled + g_vCurrMipBlurTint * currentMip, 1.0);
    #endif
#endif
}
