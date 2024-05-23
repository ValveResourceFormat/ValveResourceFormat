#version 460

#include "common/utils.glsl"

//#define F_OUTPUT_LINEAR 0
#define F_COLOR_CORRECTION_LUT 0
#define F_HAS_BLUE_NOISE 0
#define F_IS_POST_HLA 0
//#define F_BLOOM 0
//#define F_OLD_TONEMAPPING_CURVE 0 // HLA, linear only. Used in linear viewmodes to disable the tonemapping curve

// Bloom might eventually be supported
//uniform sampler2D g_tBloom; // t13
//uniform vec4 g_vBloomUvScaleClamp;
//uniform vec3 g_vNormalizedBloomStrengths;
//uniform vec3 g_vUnNormalizedBloomStrengths;




layout(location=0) uniform sampler2DMS g_tColorBuffer;

vec4 SampleColorBuffer(vec2 coords)
{
    vec4 singleSampleColor = texelFetch(g_tColorBuffer, ivec2(coords.xy), int(gl_SampleID));
    singleSampleColor.rgb = singleSampleColor.rgb / (max3(singleSampleColor.rgb) + 1.0);
    return singleSampleColor;// / 2.8;
}



uniform float g_flToneMapScalarLinear;
uniform float g_flExposureBiasScaleFactor;
uniform float g_flShoulderStrength;
uniform float g_flLinearStrength;
uniform float g_flLinearAngle;
uniform float g_flToeStrength;
uniform float g_flToeNum;
uniform float g_flToeDenom;
uniform float g_flWhitePointScale;

vec3 TonemapColor(vec3 vColor)
{
    vColor *= (g_flToneMapScalarLinear * g_flExposureBiasScaleFactor) * 2.8;

    // Uncharted tonemapper
    vec3 tonemapNumerator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength * g_flLinearAngle) + g_flToeNum * g_flToeStrength;
    vec3 tonemapDenominator = vColor * (g_flShoulderStrength * vColor + g_flLinearStrength) + g_flToeDenom * g_flToeStrength;

    vec3 vTonemappedColor = tonemapNumerator / tonemapDenominator - g_flToeNum / g_flToeDenom;

    // Divide by tonemapped white point (pre-calculated on CPU)
    vTonemappedColor *= g_flWhitePointScale; // This is actually 1/TonemapColor(WhitePoint)

    return vTonemappedColor;
}



#if F_COLOR_CORRECTION_LUT

layout(location=2) uniform sampler3D g_tColorCorrection;
uniform float g_flColorCorrectionDefaultWeight;
uniform vec2 g_vColorCorrectionColorRange = vec2(0.96875, 0.015625);
vec3 ApplyColorCorrection(vec3 vColor)
{
    vec3 scaledColor = saturate(vColor) * g_vColorCorrectionColorRange.x + g_vColorCorrectionColorRange.y;
    vec3 ColorCorrectedColor = texture(g_tColorCorrection, scaledColor).rgb;
    return mix(ColorCorrectedColor, vColor, g_flColorCorrectionDefaultWeight); // Probably for blending
}
#endif

layout(location=1) uniform sampler2D g_tBlueNoise;
uniform vec4 g_vBlueNoiseDitherParams;

//#if !F_OUTPUT_LINEAR
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
//#endif



layout(location=0) out vec4 outputColor;

void main()
{
    vec4 vColor = SampleColorBuffer(gl_FragCoord.xy);
    vColor.rgb = TonemapColor(vColor.rgb);

    // Finally, convert from Linear to Gamma space
    vColor.rgb = SrgbLinearToGamma(vColor.rgb);

#if F_COLOR_CORRECTION_LUT
    vColor.rgb = ApplyColorCorrection(vColor.rgb);
#endif

//#if !F_OUTPUT_LINEAR
    // Not present in CS2, replaced by a Film Grain setting
    vColor.rgb = DitherColor(vColor.rgb);
//#endif

    outputColor = vColor;
}
