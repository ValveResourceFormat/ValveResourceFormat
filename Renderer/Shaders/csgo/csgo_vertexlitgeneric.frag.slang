#version 460

#include "csgo_vertexlitgeneric_features.slang"

#define IBL (F_SPECULAR_INDIRECT == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../features/decal_texture_ps.slang"
#include "../features/detail_texture_ps.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tMetalness;
uniform sampler2D g_tAmbientOcclusion;

uniform float g_flBumpStrength = 1.0;
uniform float g_flOpacityScale = 1.0;
uniform float g_flAmbientOcclusionScale = 0.0;

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;
uniform bool g_bUseSecondaryUvForDecal = true;
uniform bool g_bUseSecondaryUvForTintMask = false;
uniform bool g_bUseSecondaryUvForDetailMask = true;
uniform bool g_bUseSecondaryUvForSelfIllum = false;

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
#endif

in vec2 vMeshTexCoordOut;
in vec2 vNormalTexCoordOut;

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    bool forceUv2 = (F_FORCE_UV2 == 1);

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, vNormalTexCoordOut);

    mat.Albedo = color.rgb;

    // The object alpha fades translucent surfaces
    bool translucent = (F_TRANSLUCENT == 1);
    InitOpacity(mat, color.a, g_flOpacityScale, translucent, translucent, texCoord, g_flAlphaTestReference);

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, GetUvSet(texCoord, g_bUseSecondaryUvForTintMask || forceUv2)).x);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(GetUvSet(texCoord, g_bUseSecondaryUvForSelfIllum || forceUv2), mat.Albedo);
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;

    float normalMapAmount = (F_SCALE_NORMAL_MAP == 1) ? g_flNormalMapScaleFactor : g_flBumpStrength;
    mat.NormalMap = BlendNormalMapTowardsFlat(mat.NormalMap, normalMapAmount);

    ApplyDetailTexture(mat.Albedo, mat.NormalMap, GetUvSet(texCoord, g_bUseSecondaryUvForDetailMask || forceUv2));

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // a = rimmask
    vec4 metalnessTexel = texture(g_tMetalness, texCoord);
    mat.Metalness = metalnessTexel.g;

    if (F_RETRO_REFLECTIVE == 1)
    {
        mat.RetroReflectivity = metalnessTexel.r;
    }

    vec2 occlusionCoords = GetUvSet(vMeshTexCoordOut, g_bUseSecondaryUvForAmbientOcclusion || forceUv2);
    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, occlusionCoords).r;

    // A positive scale treats the texture as centered on mid grey, so only its darker half occludes
    if (g_flAmbientOcclusionScale > 0.0)
    {
        mat.AmbientOcclusion = min(mix(1.0, 2.0 * mat.AmbientOcclusion, g_flAmbientOcclusionScale), 1.0);
    }

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

#if (F_DECAL_TEXTURE == 1)
    mat.Albedo = ApplyDecalTexture(mat.Albedo, GetUvSet(vTexCoordOut.xy, g_bUseSecondaryUvForDecal || forceUv2));
#endif

    ResolveDiffuseSpecular(mat, vec3(0.04));
    FinishMaterial(mat);

    return mat;
}

void main()
{
    // Lights are diffuse only unless the material asks for direct specular, and the occlusion texture darkens only the baked lighting
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR_DIRECT == 1;
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColor, options);
}
