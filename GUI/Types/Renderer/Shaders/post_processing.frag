#version 460

#include "common/utils.glsl"

#define F_COLOR_CORRECTION_LUT 0
//#define F_BLOOM 0
//#define F_OLD_TONEMAPPING_CURVE 0 // HLA, linear only. Used in linear viewmodes to disable the tonemapping curve

// Bloom might eventually be supported
//uniform sampler2D g_tBloom; // t13
//uniform vec4 g_vBloomUvScaleClamp;
//uniform vec3 g_vNormalizedBloomStrengths;
//uniform vec3 g_vUnNormalizedBloomStrengths;

uniform int g_nNumSamplesMSAA = 1;
uniform bool g_bFlipY = false;

uniform float g_flToneMapScalarLinear;
uniform float g_flExposureBiasScaleFactor;
uniform float g_flShoulderStrength;
uniform float g_flLinearStrength;
uniform float g_flLinearAngle;
uniform float g_flToeStrength;
uniform float g_flToeNum;
uniform float g_flToeDenom;
uniform float g_flWhitePointScale;

uniform float g_flColorCorrectionDefaultWeight = 1.0;
uniform vec2 g_vColorCorrectionColorRange = vec2(0.96875, 0.015625);

uniform vec4 g_vBlueNoiseDitherParams;

layout (location = 0) uniform sampler2DMS g_tColorBuffer;
layout (location = 1) uniform sampler3D g_tColorCorrection;
layout (location = 2) uniform sampler2D g_tBlueNoise;
layout (location = 3) uniform usampler2DMS g_tStencilBuffer;

layout (location = 0) out vec4 outputColor;

vec3 TonemapColor(vec3 vColor)
{
    vColor *= (g_flToneMapScalarLinear * g_flExposureBiasScaleFactor);

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
    vec3 ColorCorrectedColor = texture(g_tColorCorrection, scaledColor).rgb;
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

vec3 DrawOutline(vec3 vColor)
{
    const int width = 2;
    const int shadingRate = 1;

    const int NumSamples = int(g_nNumSamplesMSAA);
    const float InvNumSamples = 1.0 / float(g_nNumSamplesMSAA);

    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);
    pixelCoords.y = g_bFlipY ? textureSize(g_tStencilBuffer).y - pixelCoords.y - 1 : pixelCoords.y;

    uint insideCountCenter = 0;
    for (int i = 0; i < NumSamples; i++)
    {
        uint kernelCenter = texelFetch(g_tStencilBuffer, pixelCoords, i).r;
        insideCountCenter += kernelCenter;
    }

    if (insideCountCenter == NumSamples)
    {
        // do not draw outline inside the object
        return vColor;
    }

    uint insideCount = insideCountCenter;
    int totalSamples = NumSamples;

    for(int x = -width; x <= width;  x += shadingRate)
    {
        for(int y = -width; y <= width; y += shadingRate)
        {
            ivec2 offset = ivec2(x, y);
            if (offset == ivec2(0, 0))
            {
                continue; // already sampled (insideCountCenter)
            }

            ivec2 sampleCoords = pixelCoords + offset;
            for (int i = 0; i < NumSamples; i++)
            {
                uint stencilValue = texelFetch(g_tStencilBuffer, sampleCoords, i).r;
                insideCount += stencilValue;
                totalSamples++;
            }
        }
    }

    if (totalSamples == 0) return vColor;

    float inside = insideCount / float(totalSamples);

    float edge = RemapVal(inside, 0.001, 0.999, 0.0, 1.0);

    if (edge < 0.0 || edge > 1.0)
    {
        return vColor; // No outline
    }

    float centerContribution = insideCountCenter * InvNumSamples;
    float outlineAlpha =  RemapValClamped(edge, 0, 0.05, 0.0, 1.0) * (1.0 - centerContribution);


    const vec3 vOutlineColor = vec3(1.0, 1.0, 0.2);

    return mix(vColor.rgb, vOutlineColor, outlineAlpha);
}

vec4 SampleColorBuffer(vec2 coords)
{
    const int NumSamples = int(g_nNumSamplesMSAA);
    const float InvNumSamples = 1.0 / float(g_nNumSamplesMSAA);

    vec4 vColorMSAA = vec4(0.0);

    ivec2 pixelCoords = ivec2(coords.xy);
    pixelCoords.y = g_bFlipY ? textureSize(g_tColorBuffer).y - pixelCoords.y - 1 : pixelCoords.y;

    for (int i = 0; i < NumSamples; i++)
    {
        vec4 sampleColor = texelFetch(g_tColorBuffer, pixelCoords, i);
        sampleColor = clamp(sampleColor, vec4(0), vec4(65504));

        vColorMSAA += sampleColor.rgba * InvNumSamples;
    }

    return vColorMSAA;
}

void main()
{
    vec4 vColor = SampleColorBuffer(gl_FragCoord.xy);
    vColor.rgb = TonemapColor(vColor.rgb);

    // Finally, convert from Linear to Gamma space
    vColor.rgb = SrgbLinearToGamma(vColor.rgb);

    const bool bUseLUT = g_flColorCorrectionDefaultWeight > 0.0;
    if (bUseLUT)
    {
       vColor.rgb = ApplyColorCorrection(vColor.rgb);
    }

    // Not present in CS2, done in msaa_resolve_ps instead
    vColor.rgb = DitherColor(vColor.rgb);

    vColor.rgb = DrawOutline(vColor.rgb);

    outputColor = vColor;
}
