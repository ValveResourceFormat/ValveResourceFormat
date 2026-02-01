#version 460

#include "common/utils.slang"

uniform int g_nNumSamplesMSAA = 1;
uniform bool g_bFlipY = false;
uniform bool g_bPostProcessEnabled = true;

float InvNumSamples = 1.0 / float(g_nNumSamplesMSAA);

uniform float g_flToneMapScalarLinear;
uniform float g_flExposureBiasScaleFactor;
uniform float g_flShoulderStrength;
uniform float g_flLinearStrength;
uniform float g_flLinearAngle;
uniform float g_flToeStrength;
uniform float g_flToeNum;
uniform float g_flToeDenom;
uniform float g_flWhitePoint;
uniform float g_flWhitePointScale;

uniform float g_flColorCorrectionDefaultWeight = 1.0;
uniform vec2 g_vColorCorrectionColorRange = vec2(0.96875, 0.015625);
uniform vec4 g_vBlueNoiseDitherParams;
uniform vec2 g_vInvTexDim;

uniform float g_flOutlineSize = 1;
uniform float g_flOutlineSoftness = 0.8;
uniform float g_flOutlineIntensity = 2.25;

layout (binding = 0) uniform sampler2D g_tColorBuffer;
layout (binding = 1) uniform sampler3D g_tColorCorrectionLUT;
layout (binding = 2) uniform sampler2D g_tBlueNoise;
layout (binding = 3) uniform usampler2DMS g_tStencilBuffer;

#define D_BLOOM 0

#if (D_BLOOM == 1)
    uniform vec4 g_vBloomUvScaleClamp = vec4(1.0);
    uniform vec3 g_vUnNormalizedBloomStrengths;
    uniform vec3 g_vNormalizedBloomStrengths;
    uniform float g_flBloomLensDirtIntensity = 1;

    layout(binding = 4) uniform sampler2D g_tBloom;
    //layout(binding = 5) uniform sampler2D g_tDirt;
#endif

layout (location = 0) out vec4 outputColor;

vec3 TonemapColor(vec3 vColor)
{
    // Uncharted tonemapper
    vec3 tonemapNumerator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength * g_flLinearAngle) + g_flToeNum * g_flToeStrength;
    vec3 tonemapDenominator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength) + g_flToeDenom * g_flToeStrength;

    vec3 vTonemappedColor = tonemapNumerator / tonemapDenominator - g_flToeNum / g_flToeDenom;

    // Divide by tonemapped white point (pre-calculated on CPU)
    vTonemappedColor *= g_flWhitePointScale; // This is actually 1/TonemapColor(WhitePoint)

    return vTonemappedColor;
}

vec3 ApplyColorCorrection(vec3 vColor)
{
    vec3 scaledColor = saturate(vColor) * g_vColorCorrectionColorRange.x + g_vColorCorrectionColorRange.y;
    vec3 ColorCorrectedColor = textureLod(g_tColorCorrectionLUT, scaledColor, 0.0).rgb;
    return mix(vColor, ColorCorrectedColor, g_flColorCorrectionDefaultWeight); // Probably for blending
}

vec3 DitherColor(vec3 vColor)
{
    vec2 blueNoiseCoords = gl_FragCoord.xy * g_vBlueNoiseDitherParams.z + g_vBlueNoiseDitherParams.xy;
    vec3 blueNoise = textureLod(g_tBlueNoise, blueNoiseCoords, 0.0).rgb;

    // At this point in the shader we still have floating-point precision, which we lose when we return as uint.
    // So, we apply a 1-color-value dither to break up color banding.
    // This is part of the original code and actually works extremely well.
    vec3 subPrecisionDither = (blueNoise - 0.5) * g_vBlueNoiseDitherParams.w;
    return vColor + subPrecisionDither;
}

void SampleStencilMSAA(ivec2 pixelCoords, out uint insideCount, out int totalSamples)
{
    for (int i = 0; i < g_nNumSamplesMSAA; i++)
    {
        uint stencilValue = texelFetch(g_tStencilBuffer, pixelCoords, i).r;
        insideCount += stencilValue;
    }

    totalSamples += g_nNumSamplesMSAA;
}

vec3 DrawOutline(vec3 vColor)
{
    // base kernel width (scaled by g_flOutlineSize)
    int width = max(1, int(floor(2.5 * g_flOutlineSize + 0.5)));
    const int shadingRate = 1;

    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);
    pixelCoords.y = g_bFlipY ? textureSize(g_tStencilBuffer).y - pixelCoords.y - 1 : pixelCoords.y;

    int totalSamples = 0;
    uint insideCountCenter = 0;

    SampleStencilMSAA(pixelCoords, insideCountCenter, totalSamples);

    if (insideCountCenter == g_nNumSamplesMSAA)
    {
        // do not draw outline inside the object
        return vColor;
    }

    uint insideCount = insideCountCenter;
    uvec2 stencilTextureSize = textureSize(g_tStencilBuffer);

    // Accumulate samples in a neighborhood and also collect positional info for inside-samples
    int insideSamplePixels = 0;
    vec2 insidePosSum = vec2(0.0);
    float minDistSq = 1e9;

    for(int x = -width; x <= width;  x += shadingRate)
    {
        for(int y = -width; y <= width; y += shadingRate)
        {
            ivec2 sampleCoords = pixelCoords + ivec2(x, y);

            if (sampleCoords.x < 0 || sampleCoords.x >= stencilTextureSize.x ||
                sampleCoords.y < 0 || sampleCoords.y >= stencilTextureSize.y)
            {
                continue;
            }

            // Update global inside/total accumulators used for edge strength
            uint beforeInside = insideCount;
            SampleStencilMSAA(sampleCoords, insideCount, totalSamples);

            // Derive per-pixel coverage from the accumulator
            uint localInside = insideCount - beforeInside;
            if (localInside > 0u)
            {
                // weight by how many MSAA samples at this pixel are inside
                float w = float(localInside);
                insideSamplePixels += int(w);
                insidePosSum += vec2(sampleCoords) * w;

                vec2 d = vec2(pixelCoords) - vec2(sampleCoords);
                minDistSq = min(minDistSq, dot(d, d));
            }
        }
    }

    if (totalSamples == 0)
    {
        return vColor;
    }

    float covNeighborhood = insideCount / float(totalSamples); // 0..1 coverage in neighborhood
    float covCenter = insideCountCenter / float(g_nNumSamplesMSAA); // per-fragment coverage (0..1)

    // If there's almost no coverage nearby, skip outline
    if (covNeighborhood < 0.01)
    {
        return vColor;
    }

    // Edge strength: how much of the kernel is inside while the center is not.
    float edgeStrength = clamp(covNeighborhood - covCenter, 0.0, 1.0);
    // widen the smoothstep so weaker edges still contribute
    edgeStrength = smoothstep(0.001, 0.85 * g_flOutlineSoftness, edgeStrength);

    // Compute a smooth exterior falloff based on distance (in pixels) to the nearest inside sample
    float distPixels = sqrt(minDistSq);
    float fadeRadius = max(1.0, float(width)) * (2.6 * g_flOutlineSize) * max(0.6, g_flOutlineSoftness);
    float exteriorFade = clamp(1.0 - smoothstep(0.0, fadeRadius, distPixels), 0.0, 1.0);
    float outlineAlpha = edgeStrength * exteriorFade * g_flOutlineIntensity;
    outlineAlpha = pow(outlineAlpha, 0.75);

    // Ensure a small visible floor so very thin silhouettes remain noticeable
    outlineAlpha = max(outlineAlpha, 0.06 * edgeStrength * g_flOutlineIntensity);

    const vec3 vOutlineColor = vec3(1.0, 1.0, 0.2);
    return mix(vColor.rgb, vOutlineColor, outlineAlpha);
}


void main()
{
    vec2 fragCoord = gl_FragCoord.xy / textureSize(g_tColorBuffer, 0);
    vec4 vColor = textureLod(g_tColorBuffer, fragCoord, 0.0);

    if (g_bPostProcessEnabled)
    {
        vColor *= (g_flToneMapScalarLinear * g_flExposureBiasScaleFactor);
        vec3 vRawColor = vColor.rgb;

        #if (D_BLOOM == 1)
            vec3 bloomSample = textureLod(g_tBloom, (min(fragCoord, g_vBloomUvScaleClamp.zw) * g_vBloomUvScaleClamp.xy).xy, 0.0).xyz;

            // lens dirt
            // bloomSample += ((bloomSample * textureLod(g_tDirt, fragCoord.xy, 0.0).xyz) * g_flBloomLensDirtIntensity);

            // BLUR bloom
            // this looks just wrong and im not sure why
            // very few maps use this anyways, it looks weird to begin with
            vColor.rgb *= 1.0 - g_vUnNormalizedBloomStrengths.z;
            vColor.rgb += bloomSample.xyz * g_vNormalizedBloomStrengths.z * vRawColor.rgb;

            // ADD bloom
            vColor.rgb += (bloomSample.rgb * g_vNormalizedBloomStrengths.x);

            //outputColor.rgb = SrgbLinearToGamma(bloomSample);
            //return;
        #endif

        vColor.rgb = TonemapColor(vColor.rgb);
        vColor.rgb = SrgbLinearToGamma(vColor.rgb);
        
        #if (D_BLOOM == 1)
            // SCREEN bloom
            vec3 screenBloomSample = bloomSample.rgb * vRawColor.rgb;
            vec3 screenBlend = saturate((screenBloomSample / (screenBloomSample + 0.187)) * 1.034);
            screenBlend *= g_vNormalizedBloomStrengths.y;

            vColor.rgb = (vColor.rgb + screenBlend) - (vColor.rgb * screenBlend);
        #endif

        if (g_flColorCorrectionDefaultWeight > 0.0)
        {
            vColor.rgb = ApplyColorCorrection(vColor.rgb);
        }

        vColor.rgb = DitherColor(vColor.rgb);
    }

    vColor.rgb = DrawOutline(vColor.rgb);
    outputColor = vColor;
}
