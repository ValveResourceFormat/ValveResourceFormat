#version 460

// The entry file includes its game's features file first and defines GetMaterial() from GetVrComplexMaterial(), with
// the bump strength its game supports.

#define UNLIT (F_UNLIT == 1)

// Rect lights shade as area lights only with specular, otherwise they light from their centre
#define BARN_LIGHT_AREA_LIGHTS (F_SPECULAR == 1)
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
#define IBL (F_SPECULAR == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP (F_DIFFUSE_WRAP == 1)
#define ANISO_CUBEMAP_WARP_OPTIONAL 1
#define ANISO_CUBEMAP_WARP_VERTEX_FRAME 1
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
    uniform sampler2D g_tMetalness;
#else
    uniform float g_flMetalness = 0.0;
    uniform float g_flRetroReflectivity = 1.0;
#endif

uniform float g_flAmbientOcclusionDirectDiffuse = 0.0;
uniform float g_flAmbientOcclusionDirectSpecular = 0.0;

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
#endif

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;
uniform bool g_bUseSecondaryUvForDetailMask = true;
uniform bool g_bUseSecondaryUvForSelfIllum = false;

#if (F_FOLIAGE_ANIMATION > 0)
    #include "../common/surface_foliage.slang"
#endif

MaterialProperties_t GetVrComplexMaterial(vec2 texCoord, vec3 vertexNormals, float bumpStrength)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    MaterialProperties_t mat = InitGenericMaterial(texCoord, vertexNormals, color, true);

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(GetUvSet(texCoord, g_bUseSecondaryUvForSelfIllum), mat.Albedo);
#endif

#if (UNLIT)
    #if (F_METALNESS_TEXTURE == 0)
        // The metallic part of the albedo is dropped even when unlit
        mat.Albedo *= 1.0 - g_flMetalness;
    #endif

    FinishUnlitMaterial(mat);
    return mat;
#endif

    DecodeGenericNormal(mat, texCoord, normalTexture, bumpStrength);

    ApplyDetailTexture(mat.Albedo, mat.NormalMap, GetUvSet(texCoord, g_bUseSecondaryUvForDetailMask));

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_METALNESS_TEXTURE == 1)
    vec4 metalnessTexel = texture(g_tMetalness, texCoord);

    #if (F_ALPHA_TEST == 0) && !(TRANSLUCENT)
        // Opaque materials keep metalness in color alpha, unless their metalness texture also holds the retro-reflective mask
        if (F_RETRO_REFLECTIVE == 1)
        {
            mat.Metalness = metalnessTexel.g;
            mat.RetroReflectivity = metalnessTexel.r;
        }
        else
        {
            mat.Metalness = color.a;
        }
    #else
        mat.Metalness = metalnessTexel.g;
    #endif
#else
    mat.Metalness = g_flMetalness;
    mat.RetroReflectivity = (F_RETRO_REFLECTIVE == 1) ? g_flRetroReflectivity : 0.0;
#endif

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;

#if (F_FOLIAGE_ANIMATION > 0)
    ApplyFoliageOcclusion(mat);
#endif

    mat.ClothMask = (F_CLOTH_SHADING == 1) ? 1.0 : 0.0;

    FinishGenericMaterial(mat, texCoord);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR == 1;
    options.DirectDiffuseAO = g_flAmbientOcclusionDirectDiffuse;
    options.DirectSpecularAO = g_flAmbientOcclusionDirectSpecular;

#if (UNLIT)
    bool outputWritten = RenderUnlitSurface(g_tColor, options);
#else
    bool outputWritten = RenderSurface(g_tColor, options);
#endif

    if (outputWritten)
    {
        return;
    }

#if (F_FOLIAGE_ANIMATION > 0) && (S_MODE_DEPTH == 0)
    if (g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = vFoliageParamsOut.rgb;
    }
#endif
}
