#version 460

#ifndef VRF_SURFACE_CSGO_CHARACTER_INCLUDE
#define VRF_SURFACE_CSGO_CHARACTER_INCLUDE

// Adjustments that character and weapon materials ask for with F_ENABLE_ADJUSTMENTS, a detail texture and color and
// roughness corrections. Include it after surface_shading.slang. The shader declares F_ENABLE_ADJUSTMENTS and
// F_DETAIL_TEXTURE, and applies the adjustments from its GetMaterial(), since it blends the detail in its own way.

// Rotates the hue by an angle in radians, less so for colors close to grey
vec3 ShiftCharacterHue(vec3 color, float angle)
{
    float colorMax = Max3(color);
    float colorRange = colorMax == 0.0 ? 0.0 : (colorMax - Min3(color)) / colorMax;

    return mix(vec3(GetLuma(color)), RotateHue(color, angle), vec3(pow(colorRange, 0.125)));
}

vec3 AdjustCharacterColor(vec3 color, float brightness, float contrast, float hueShift, float saturation)
{
    color = ContrastBrightness(color, contrast, brightness);

    // Hue shifts are authored in degrees
    vec3 hueShifted = ShiftCharacterHue(color, radians(hueShift));

    return saturate(mix(vec3(GetLuma(hueShifted)), hueShifted, vec3(saturation)));
}

#if (F_ENABLE_ADJUSTMENTS == 1)

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

// Contrast and brightness of the roughness texture, only where the tint mask is set when the material masks them
vec2 AdjustCharacterRoughness(vec2 roughnessTex, float tintStrength)
{
    float tintAmount = g_bMaskRoughnessAdjustmentsByTintMask ? tintStrength : 1.0;
    float roughnessContrast = mix(1.0, g_fTextureRoughnessContrast, tintAmount);
    float roughnessBrightness = mix(1.0, g_fTextureRoughnessBrightness, tintAmount);

    return ContrastBrightness(roughnessTex, roughnessContrast, roughnessBrightness);
}

vec3 BlendCharacterDetail(vec3 albedo, vec3 detail, float detailBlendFactor)
{
    vec3 detailBlended = (F_DETAIL_TEXTURE == 1) ? detail : albedo * detail;
    return mix(albedo, detailBlended, vec3(detailBlendFactor));
}

#endif

// The albedo and opacity of the color texture, tinted by the vertex tint or by the tint mask. Returns the tint strength.
float InitCharacterColor(inout MaterialProperties_t mat, vec4 color, sampler2D tintMask, vec2 texCoord, float opacityScale, float alphaTestReference)
{
    mat.Albedo = color.rgb;
    InitOpacity(mat, color.a, opacityScale, F_TRANSLUCENT == 1, false, texCoord, alphaTestReference);

    float tintStrength = 1.0;
    vec3 tint = vVertexColorOut.rgb;
#if (F_TINT_MASK == 1)
    tintStrength = texture(tintMask, texCoord).x;
    tint = GetMaskedVertexTint(tintStrength);
#endif
    mat.Albedo *= tint;

    return tintStrength;
}

// The world normal of a normal map stored as two components
void InitCharacterNormal(inout MaterialProperties_t mat, vec2 encodedNormal)
{
    mat.NormalMap = DecodeHemiOctahedronNormal(encodedNormal);
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
}

#endif
