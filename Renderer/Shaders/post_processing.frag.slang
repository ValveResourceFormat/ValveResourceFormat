#version 460

#include "common/utils.slang"

uniform bool g_bPostProcessEnabled = true;

const int TONEMAP_ENGINE_CURVE = 0;
const int TONEMAP_NONE = 1;
const int TONEMAP_GAME_CURVE = 2;

// The games that skip post processing tonemap in their materials, with no exposure bias
uniform int g_nTonemapMode = 0;

// Whether the exposed color is limited to the white point before the curve
uniform bool g_bClampToWhitePoint = true;

// Only the exposure scalar and the sRGB encode: no exposure bias, bloom, tonemap, color correction, gamma or dither
uniform bool g_bRawOutput = false;

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

uniform sampler2D g_tColorBuffer;
uniform sampler3D g_tColorCorrectionLUT;
uniform sampler2D g_tBlueNoise; // reserved

#define D_BLOOM 0

#if (D_BLOOM == 1)
    uniform vec4 g_vBloomUvScaleClamp = vec4(1.0);
    uniform vec3 g_vUnNormalizedBloomStrengths;
    uniform vec3 g_vNormalizedBloomStrengths;

    // Whether the bloom was thresholded from the exposed scene, so it already carries the exposure
    uniform bool g_bBloomExposed = false;

    uniform sampler2D g_tBloom;
#endif

layout (location = 0) out vec4 outputColor;

vec3 TonemapColor(vec3 vColor)
{
    vColor *= 2.8;

    if (g_bClampToWhitePoint)
    {
        vColor = min(vColor, vec3(g_flWhitePoint));
    }

    // Uncharted tonemapper
    vec3 tonemapNumerator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength * g_flLinearAngle) + g_flToeNum * g_flToeStrength;
    vec3 tonemapDenominator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength) + g_flToeDenom * g_flToeStrength;

    vec3 vTonemappedColor = tonemapNumerator / tonemapDenominator - g_flToeNum / g_flToeDenom;

    // Divide by tonemapped white point (pre-calculated on CPU)
    vTonemappedColor *= g_flWhitePointScale; // This is actually 1/TonemapColor(WhitePoint)

    return vTonemappedColor;
}

// Fixed rational curve of a game that has no tonemap settings
vec3 GameTonemapColor(vec3 vColor)
{
    vec3 x = vColor * 0.6;
    return x * (15.8 * x + 2.12) / (x * (1.2 * x + 5.92) + 1.9);
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
        if (g_bRawOutput)
        {
            outputColor = vec4(SrgbLinearToGamma(saturate(vColor.rgb * g_flToneMapScalarLinear)), vColor.a);
            return;
        }

        // The bloom terms are weighted by the exposure on its own, not by the exposed color, so keep it
        // around after the scene has been multiplied through.
        float flExposure = g_flToneMapScalarLinear;
        if (g_nTonemapMode == TONEMAP_ENGINE_CURVE)
        {
            flExposure *= g_flExposureBiasScaleFactor;
        }

        vColor.rgb *= flExposure;

        #if (D_BLOOM == 1)
            vec3 bloomSample = textureLod(g_tBloom, min(fragCoord, g_vBloomUvScaleClamp.zw) * g_vBloomUvScaleClamp.xy, 0.0).xyz;

            // BLUR bloom
            float flBlurBloomExposure = g_bBloomExposed ? 1.0 : flExposure;
            vColor.rgb *= 1.0 - g_vUnNormalizedBloomStrengths.z;
            vColor.rgb += bloomSample * (g_vNormalizedBloomStrengths.z * flBlurBloomExposure);

            // ADD bloom, not weighted by exposure
            vColor.rgb += (bloomSample * g_vNormalizedBloomStrengths.x);
        #endif

        if (g_nTonemapMode == TONEMAP_ENGINE_CURVE)
        {
            vColor.rgb = TonemapColor(vColor.rgb);
        }
        else if (g_nTonemapMode == TONEMAP_GAME_CURVE)
        {
            vColor.rgb = GameTonemapColor(vColor.rgb);
        }

        vColor.rgb = SrgbLinearToGamma(vColor.rgb);
        
        #if (D_BLOOM == 1)
            // SCREEN bloom, blended against the gamma encoded color. Exposed bloom gets the exposure a second time.
            vec3 screenBloomSample = bloomSample * flExposure;
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
