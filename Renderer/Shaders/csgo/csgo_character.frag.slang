#version 460

#include "csgo_character_features.slang"

#define IBL 1
#define SUBSURFACE (F_SUBSURFACE_SCATTERING == 1)
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../features/decal_texture_ps.slang"
#include "../common/surface_csgo_character.slang"
#include "../features/csgo_character_eyes_ps.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tMetalness;
uniform sampler2D g_tAmbientOcclusion;
uniform sampler2D g_tSssMask;

uniform float g_flOpacityScale = 1.0;

uniform float g_flSheenScale = 0.667;
uniform vec3 g_flSheenTintColor = vec3(1.0); // SrgbRead(true)

#if (F_ENABLE_ADJUSTMENTS == 1)
    uniform float g_fDetailBrightness = 1.0;
    uniform float g_fDetailContrast = 1.0;
    uniform float g_fDetailHueShift = 0.0;
    uniform float g_fDetailSaturation = 1.0;
    uniform int g_nMaskDetailTextureByTintMask = 0; // 0 = None, 1 = Tint Mask, 2 = Inverse Tint Mask
#endif

vec3 GetClothSheenColor(float reflectance, vec3 albedo, float clothMask)
{
    return mix(vec3(reflectance), saturate((g_flSheenTintColor.rgb * sqrt(albedo)) * g_flSheenScale), clothMask);
}

#if (F_ENABLE_ADJUSTMENTS == 1)

void ApplyCharacterAdjustments(inout MaterialProperties_t mat, float tintStrength)
{
    mat.RoughnessTex = AdjustCharacterRoughness(mat.RoughnessTex, tintStrength);

    vec3 detail = texture(g_tDetail, vDetailTexCoords).rgb;
    float detailBlendFactor = g_fDetailBlendFactor;

    vec3 adjustedAlbedo = AdjustCharacterColor(mat.Albedo, g_fBrightness, g_fContrast, g_fHueShift, g_fSaturation);
    vec3 albedo = mix(mat.Albedo, adjustedAlbedo, vec3(tintStrength));

    detail = AdjustCharacterColor(detail, g_fDetailBrightness, g_fDetailContrast, g_fDetailHueShift, g_fDetailSaturation);

    if (g_nMaskDetailTextureByTintMask == 1)
    {
        detailBlendFactor *= tintStrength;
    }
    else if (g_nMaskDetailTextureByTintMask == 2)
    {
        detailBlendFactor *= 1.0 - tintStrength;
    }

    mat.Albedo = BlendCharacterDetail(albedo, detail, detailBlendFactor);
}

#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

#if (F_SUBSURFACE_SCATTERING == 1)
    mat.SSSMask = texture(g_tSssMask, texCoord).g;
#endif

    float tintStrength = InitCharacterColor(mat, color, g_tTintMask, texCoord, g_flOpacityScale, g_flAlphaTestReference);

    InitCharacterNormal(mat, normalTexture.rg);
#if (F_ANISOTROPIC_GLOSS == 1)
    mat.RoughnessTex.xy = texture(g_tAnisoGloss, texCoord).rg;
#else
    mat.RoughnessTex.xy = normalTexture.bb;
#endif

    // a = rimmask
    vec4 metalnessTexel = texture(g_tMetalness, texCoord);
    mat.Metalness = metalnessTexel.g;

    if (F_RETRO_REFLECTIVE == 1)
    {
        mat.RetroReflectivity = metalnessTexel.r;
    }

    mat.ClothMask = metalnessTexel.b * (1.0 - metalnessTexel.g);

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;

#if (F_ENABLE_ADJUSTMENTS == 1)
    ApplyCharacterAdjustments(mat, tintStrength);
#endif

    AdjustRoughnessByGeometricNormal(mat);

#if (F_EYEBALLS == 1)
    ApplyEye(eyeInterpolator, texCoord, mat);
#endif

    ResolveAmbientNormal(mat, texCoord);

#if (F_DECAL_TEXTURE == 1)
    mat.Albedo = ApplyDecalTexture(mat.Albedo, vTexCoordOut.xy);
#endif

    vec3 F0 = vec3(0.04);

    if (F_CLOTH_SHADING == 1)
    {
        F0 = GetClothSheenColor(0.04, mat.Albedo, mat.ClothMask);
    }

    ResolveDiffuseSpecular(mat, F0);
    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
