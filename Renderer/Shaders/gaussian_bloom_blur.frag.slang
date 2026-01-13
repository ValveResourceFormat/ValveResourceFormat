#version 460

// D_BLUR_PASS = 1 -> separable gaussian blur for bluring mips while downsampling
// D_BLUR_PASS = 2 -> 9 sample tent filter upsampling for summing up mips after blurring + apply strength and tint
// D_BLUR_PASS = 3 -> same as 2 but takes a pair of strength and tint for first and second mip

#define D_BLUR_PASS 0
#define D_BLUR_PASS_HORIZONTAL 0

uniform vec2 g_vTexelSize;
uniform vec2 g_vTextureSize;
uniform float g_nCurrentMip;

layout(binding = 0) uniform sampler2D g_tSource;
layout(location = 0) out vec4 outputColor;

#if D_BLUR_PASS == 1
    const float pixelOffsets[4] = { -2.196197f, -0.430063f, 1.0f, 2.196197f };
    const float gaussianWeights[4] = { 0.121596f, 0.529213f, 0.227595f, 0.121596f };
#elif D_BLUR_PASS >= 2
    #if D_BLUR_PASS == 3
        uniform vec3 g_vPrevMipBlurTint;
    #endif
    uniform vec3 g_vCurMipBlurTint = vec3(1.0f);
    // we dont need this, can reuse texel size, uv clamp is needed because they keep the bloom buffer at a fixed 512x512 at 1080p
    //uniform vec4 g_vCombineUVScaleAndClamp;
#endif

void main()
{

#if D_BLUR_PASS == 1

    vec3 blurOutput = vec3(0.0);
    for (int i = 0; i < 4; i++)
    {
        vec2 offset;

    #if D_BLUR_PASS_HORIZONTAL
        offset = gl_FragCoord.xy + vec2(pixelOffsets[i], 0.0f);
    #else
        offset = gl_FragCoord.xy + vec2(0.0f, pixelOffsets[i]);
    #endif
       
        vec2 samplePos = min(offset, g_vTextureSize);

        blurOutput += (textureLod(g_tSource, samplePos * g_vTexelSize.xy, 0.0f).xyz * gaussianWeights[i]);
    }
    outputColor = vec4(blurOutput.xyz, 1.0f);

    return;

    // blur pass 2 is actually a tent upsampling filter used to upsample the blured mips to the final bloom image
#elif D_BLUR_PASS >= 2

    vec2 uvScale = g_vTexelSize.xy;
    vec2 uvClamp = vec2(1.0f);//g_vCombineUVScaleAndClamp.zw;

    vec2 filterRadius = vec2(1.0f);
    vec2 currentUV = uvScale * gl_FragCoord.xy;
    vec2 minUV = uvScale * (gl_FragCoord.xy - filterRadius);
    vec2 maxUV = uvScale * (gl_FragCoord.xy + filterRadius);
    
    vec2 uv_topLeft = min(minUV, uvClamp);
    vec2 uv_topRight = min(vec2(maxUV.x, minUV.y), uvClamp);
    vec2 uv_bottomRight = min(maxUV, uvClamp);
    vec2 uv_bottomLeft = min(vec2(minUV.x, maxUV.y), uvClamp);
    vec2 uv_center = min(currentUV, uvClamp);
    
    // tent filter, weighted sum of 9 samples
    // centrer sample
    vec3 higherMipColor = textureLod(g_tSource, uv_center, g_nCurrentMip - 1.0).xyz * g_vCurMipBlurTint;

    vec3 g_nCurrentMipColor = textureLod(g_tSource, uv_center, g_nCurrentMip).xyz * 0.25;

    // corner samples
    g_nCurrentMipColor += textureLod(g_tSource, uv_topLeft, g_nCurrentMip).xyz * 0.0625;
    g_nCurrentMipColor += textureLod(g_tSource, uv_topRight, g_nCurrentMip).xyz * 0.0625;
    g_nCurrentMipColor += textureLod(g_tSource, uv_bottomRight, g_nCurrentMip).xyz * 0.0625;
    g_nCurrentMipColor += textureLod(g_tSource, uv_bottomLeft, g_nCurrentMip).xyz * 0.0625;

    // edge samples
    g_nCurrentMipColor += textureLod(g_tSource, vec2(currentUV.x, uv_topLeft.y), g_nCurrentMip).xyz * 0.125;
    g_nCurrentMipColor += textureLod(g_tSource, vec2(currentUV.x, uv_bottomRight.y), g_nCurrentMip).xyz * 0.125;
    g_nCurrentMipColor += textureLod(g_tSource, vec2(uv_topLeft.x, currentUV.y), g_nCurrentMip).xyz * 0.125;
    g_nCurrentMipColor += textureLod(g_tSource, vec2(uv_bottomRight.x, currentUV.y), g_nCurrentMip).xyz * 0.125;

    // on first pass we want to multiply with previous mip blur tint
    // since the first mip wont get tinted otherwise
    #if D_BLUR_PASS == 3
        g_nCurrentMipColor *= g_vPrevMipBlurTint;
    #endif

    outputColor = vec4(higherMipColor + g_nCurrentMipColor, 1.0);
    return;

#endif

    vec2 sampleCoord = ((gl_FragCoord.xy) * g_vTexelSize).xy;
    vec4 textureSample = clamp(textureLod(g_tSource, sampleCoord, g_nCurrentMip), 0.0f, 65504.0f);
    
    outputColor = vec4(textureSample.xyz, 1.0f);
}
