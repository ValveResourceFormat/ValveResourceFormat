#version 460

#include "csgo_complex_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
#define TRANSMISSIVE_GEOMETRIC_NORMAL 1
#define ALPHA_TEST_COVERAGE_BY_UV_FOOTPRINT 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"
#include "../features/detail_texture_ps.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;

#if (F_METALNESS_TEXTURE == 1)
    #if (F_ALPHA_TEST == 1) || (TRANSLUCENT)
        uniform sampler2D g_tMetalness;
    #endif
#else
    uniform float g_flMetalness = 0.0;
#endif

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
#endif

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;
uniform bool g_bUseSecondaryUvForDetailMask = true;
uniform bool g_bUseSecondaryUvForSelfIllum = false;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    // Only translucency fades by the vertex alpha, not the alpha test
    MaterialProperties_t mat = InitGenericMaterial(texCoord, vertexNormals, color, F_TRANSLUCENT == 1);

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(GetUvSet(texCoord, g_bUseSecondaryUvForSelfIllum), mat.Albedo);
#endif

    DecodeGenericNormal(mat, texCoord, normalTexture, 1.0);

    ApplyDetailTextureWithOptions(mat.Albedo, mat.NormalMap, GetUvSet(texCoord, g_bUseSecondaryUvForDetailMask), false, false, true);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_METALNESS_TEXTURE == 1)
    #if (F_ALPHA_TEST == 1) || (TRANSLUCENT)
        mat.Metalness = texture(g_tMetalness, texCoord).g;
    #else
        mat.Metalness = color.a;
    #endif
#else
    mat.Metalness = g_flMetalness;
#endif

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;

    FinishGenericMaterial(mat, texCoord);

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    // The transmissive texture takes the vertex tint, the albedo already has it
    if (F_USE_ALBEDO_FOR_TRANSMISSIVE == 0)
    {
        mat.TransmissiveColor *= vVertexColorOut.rgb;
    }
#endif

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    options.FogFadesAlpha = (F_ADDITIVE_BLEND == 1);
    RenderSurface(g_tColor, options);
}
