#version 460

#include "common/utils.slang"

uniform bool g_bFlipY = false;
uniform bool g_bPostProcessEnabled = true;

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
uniform float g_flFullScreenGamma = 2.2;

uniform float g_flColorCorrectionDefaultWeight = 1.0;
uniform vec2 g_vColorCorrectionColorRange = vec2(0.96875, 0.015625);
uniform vec4 g_vBlueNoiseDitherParams;
uniform vec2 g_vInvTexDim;

uniform sampler2D g_tColorBuffer;
uniform sampler3D g_tColorCorrectionLUT;
uniform sampler2D g_tBlueNoise; // reserved

#define D_BLOOM 0

#if (D_BLOOM == 1)
    uniform vec4 g_vBloomUvScaleClamp = vec4(1.0);
    uniform vec3 g_vUnNormalizedBloomStrengths;
    uniform vec3 g_vNormalizedBloomStrengths;
    uniform float g_flBloomLensDirtIntensity = 1;

    uniform sampler2D g_tBloom;
    //uniform sampler2D g_tDirt;
#endif

layout (location = 0) out vec4 outputColor;

vec3 TonemapColor(vec3 vColor)
{
    vColor = min(vColor * 2.8, vec3(g_flWhitePoint));

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

void main()
{
    vec2 fragCoord = gl_FragCoord.xy / textureSize(g_tColorBuffer, 0);
    vec4 vColor = textureLod(g_tColorBuffer, fragCoord, 0.0);

    if (g_bPostProcessEnabled)
    {
        // The bloom terms are weighted by the exposure on its own, not by the exposed color, so keep it
        // around after the scene has been multiplied through.
        float flExposure = g_flToneMapScalarLinear * g_flExposureBiasScaleFactor;
        vColor.rgb *= flExposure;

        #if (D_BLOOM == 1)
            vec3 bloomSample = textureLod(g_tBloom, (min(fragCoord, g_vBloomUvScaleClamp.zw) * g_vBloomUvScaleClamp.xy).xy, 0.0).xyz;

            // lens dirt
            // bloomSample += ((bloomSample * textureLod(g_tDirt, fragCoord.xy, 0.0).xyz) * g_flBloomLensDirtIntensity);

            // BLUR bloom
            vColor.rgb *= 1.0 - g_vUnNormalizedBloomStrengths.z;
            vColor.rgb += bloomSample * (g_vNormalizedBloomStrengths.z * flExposure);

            // ADD bloom, not weighted by exposure
            vColor.rgb += (bloomSample.rgb * g_vNormalizedBloomStrengths.x);

            //outputColor.rgb = SrgbLinearToGamma(bloomSample);
            //return;
        #endif

        vColor.rgb = TonemapColor(vColor.rgb);
        vColor.rgb = SrgbLinearToGamma(vColor.rgb);
        
        #if (D_BLOOM == 1)
            // SCREEN bloom, blended against the gamma encoded color
            vec3 screenBloomSample = bloomSample.rgb * flExposure;
            vec3 screenBlend = saturate((screenBloomSample / (screenBloomSample + 0.187)) * 1.035);
            screenBlend *= g_vNormalizedBloomStrengths.y;

            vColor.rgb = (vColor.rgb + screenBlend) - (vColor.rgb * screenBlend);
        #endif

        if (g_flColorCorrectionDefaultWeight > 0.0)
        {
            vColor.rgb = ApplyColorCorrection(vColor.rgb);
        }

        vColor.rgb = pow(ClampToPositive(vColor.rgb), vec3(g_flFullScreenGamma * (1.0 / 2.2)));
        vColor.rgb = DitherColor(vColor.rgb);
    }

    outputColor = vColor;
}
