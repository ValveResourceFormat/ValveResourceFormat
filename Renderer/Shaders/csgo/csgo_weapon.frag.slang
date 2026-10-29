#version 460

#include "csgo_weapon_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_csgo_character.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tMetalness;
uniform sampler2D g_tAmbientOcclusion;

uniform float g_flOpacityScale = 1.0;

uniform vec2 g_vMetalnessRemapRange = vec2(0.0);
uniform float g_flMetalnessTransitionBias = 2.0;
uniform float g_flPearlescentScale = 0.0;

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
#endif

#if (F_ENABLE_ADJUSTMENTS == 1)
    uniform bool g_bMaskDetailTextureByTintMask = false;

    void ApplyWeaponAdjustments(inout MaterialProperties_t mat, float tintStrength)
    {
        mat.RoughnessTex = AdjustCharacterRoughness(mat.RoughnessTex, tintStrength);

        vec3 detail = texture(g_tDetail, vDetailTexCoords).rgb;
        float detailBlendFactor = g_fDetailBlendFactor;

        if (g_bMaskDetailTextureByTintMask)
        {
            detailBlendFactor *= tintStrength;
        }

        // Weapons adjust the whole color, after the detail texture
        vec3 albedo = BlendCharacterDetail(mat.Albedo, detail, detailBlendFactor);
        mat.Albedo = AdjustCharacterColor(albedo, g_fBrightness, g_fContrast, g_fHueShift, g_fSaturation);
    }
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    float tintStrength = InitCharacterColor(mat, color, g_tTintMask, texCoord, g_flOpacityScale, g_flAlphaTestReference);

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);
#endif

    InitCharacterNormal(mat, normalTexture.rg);

    // b = sfx mask
    vec4 metalnessTexel = texture(g_tMetalness, texCoord);
    mat.Metalness = mix(g_vMetalnessRemapRange.x, g_vMetalnessRemapRange.y, metalnessTexel.g);
    mat.RoughnessTex.xy = metalnessTexel.rr;

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;

#if (F_ENABLE_ADJUSTMENTS == 1)
    ApplyWeaponAdjustments(mat, tintStrength);
#endif

    if (g_flPearlescentScale != 0.0)
    {
        float pearlescentAngle = g_flPearlescentScale * (1.0 - dot(mat.ViewDir, mat.Normal)) * metalnessTexel.b;
        mat.Albedo = ShiftCharacterHue(mat.Albedo, pearlescentAngle);
    }

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);
    ResolveDiffuseSpecular(mat, vec3(0.02));

    mat.DiffuseColor = mat.Albedo * pow(1.0 - mat.Metalness, g_flMetalnessTransitionBias);

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
