#version 460

#include "vr_simple_markable_features.slang"

#define SELF_ILLUM ((F_SELF_ILLUM == 1) && (GameVfx_vr_bloody_simple == 1))
#define PARALLAX_OCCLUSION (GameVfx_vr_parallax_occlusion == 1)
#define FADE_OPAQUE_BY_VERTEX_ALPHA ((GameVfx_vr_bloody_simple == 0) && (GameVfx_vr_power_cables == 0))

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

#if (SELF_ILLUM)
    #include "../features/self_illum_ps.slang"

    #if (F_SECONDARY_UV == 1)
        uniform bool g_bUseSecondaryUvForSelfIllum = false;
    #endif
#endif

#if (PARALLAX_OCCLUSION)
    #include "../features/parallax_occlusion_ps.slang"
#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

#if (PARALLAX_OCCLUSION)
    texCoord = ApplyParallaxOcclusion(texCoord, vNormalOut);
#endif

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

#if (TRANSLUCENT) || (F_ALPHA_TEST == 1)
    mat.Opacity = color.a;
#elif (FADE_OPAQUE_BY_VERTEX_ALPHA)
    DiscardByVertexFade();
#endif

#if (TRANSLUCENT)
    mat.Opacity *= g_flOpacityScale;
#endif

#if (F_ALPHA_TEST == 1)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord, g_flAlphaTestReference);
#endif

    ApplyVertexTint(mat, texCoord);

#if (SELF_ILLUM)
    mat.IllumColor = SampleSelfIllumination(GetUvSet(texCoord, g_bUseSecondaryUvForSelfIllum), mat.Albedo);
#endif

    DecodeGenericNormal(mat, texCoord, normalTexture, 1.0);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Metalness = color.a;

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;

    FinishGenericMaterial(mat, texCoord);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColor, options);
}
