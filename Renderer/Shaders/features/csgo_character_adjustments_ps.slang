#version 460

#ifndef VRF_CSGO_CHARACTER_ADJUSTMENTS_PS_INCLUDE
#define VRF_CSGO_CHARACTER_ADJUSTMENTS_PS_INCLUDE

#if CS2_CHARACTER_ADJUSTMENTS
in vec2 vDetailTexCoords;

uniform sampler2D g_tDetail;

uniform float g_fBrightness = 1.0;
uniform float g_fContrast = 1.0;
uniform float g_fHueShift = 0.0;
uniform float g_fSaturation = 1.0;

uniform float g_fTextureRoughnessBrightness = 1.0;
uniform float g_fTextureRoughnessContrast = 1.0;
uniform bool g_bMaskRoughnessAdjustmentsByTintMask = false;

// F_DETAIL_TEXTURE picks the detail blend: 0 = Multiply, 1 = Replace
uniform float g_fDetailBlendFactor = 1.0;
uniform float g_fDetailBrightness = 1.0;
uniform float g_fDetailContrast = 1.0;
uniform float g_fDetailHueShift = 0.0;
uniform float g_fDetailSaturation = 1.0;
uniform int g_nMaskDetailTextureByTintMask = 0; // 0 = None, 1 = Tint Mask, 2 = Inverse Tint Mask

vec3 AdjustCharacterColor(vec3 color, float brightness, float contrast, float hueShift, float saturation)
{
    color = ContrastBrightness(color, contrast, brightness);

    float colorMax = max3(color);
    float colorRange = colorMax == 0.0 ? 0.0 : (colorMax - min3(color)) / colorMax;

    // Hue shifts are authored in degrees
    vec3 hueShifted = mix(vec3(GetLuma(color)), RotateHue(color, radians(hueShift)), vec3(pow(colorRange, 0.125)));

    return saturate(mix(vec3(GetLuma(hueShifted)), hueShifted, vec3(saturation)));
}

void ApplyCharacterAdjustments(inout MaterialProperties_t mat, float tintStrength)
{
    float roughnessContrast = g_bMaskRoughnessAdjustmentsByTintMask ? mix(1.0, g_fTextureRoughnessContrast, tintStrength) : g_fTextureRoughnessContrast;
    float roughnessBrightness = g_bMaskRoughnessAdjustmentsByTintMask ? mix(1.0, g_fTextureRoughnessBrightness, tintStrength) : g_fTextureRoughnessBrightness;
    mat.RoughnessTex = ContrastBrightness(mat.RoughnessTex, roughnessContrast, roughnessBrightness);

    vec3 albedo = mix(mat.Albedo, AdjustCharacterColor(mat.Albedo, g_fBrightness, g_fContrast, g_fHueShift, g_fSaturation), vec3(tintStrength));

    vec3 detail = AdjustCharacterColor(texture(g_tDetail, vDetailTexCoords).rgb, g_fDetailBrightness, g_fDetailContrast, g_fDetailHueShift, g_fDetailSaturation);

    float detailBlendFactor = g_fDetailBlendFactor;
    if (g_nMaskDetailTextureByTintMask == 1)
    {
        detailBlendFactor *= tintStrength;
    }
    else if (g_nMaskDetailTextureByTintMask == 2)
    {
        detailBlendFactor *= 1.0 - tintStrength;
    }

    vec3 detailBlended = (F_DETAIL_TEXTURE == 1) ? detail : albedo * detail;
    mat.Albedo = mix(albedo, detailBlended, vec3(detailBlendFactor));
}
#endif

#endif
