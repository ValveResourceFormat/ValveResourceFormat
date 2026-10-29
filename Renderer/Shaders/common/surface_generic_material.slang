#version 460

#ifndef VRF_SURFACE_GENERIC_MATERIAL_INCLUDE
#define VRF_SURFACE_GENERIC_MATERIAL_INCLUDE

// The material of a surface with a color texture and a normal map that holds the roughness, shared by the shaders
// that differ only in where the metalness and ambient occlusion come from. Include it after surface_shading.slang.
// A shader with steps of its own between the shared ones calls InitGenericMaterial(), DecodeGenericNormal() and
// FinishGenericMaterial() instead of GetGenericMaterial().

#include "../features/decal_texture_ps.slang"
#include "../features/edge_glass_ps.slang"

uniform sampler2D g_tTintMask;

uniform float g_flOpacityScale = 1.0;

uniform vec3 g_vDiffuseWrapColor = vec3(1.0, 0.5, 0.3);

uniform bool g_bUseSecondaryUvForTintMask = false;

#if (F_DECAL_TEXTURE == 1)
    uniform bool g_bUseSecondaryUvForDecal = true;
#endif

uniform float g_flAlphaTestReference = 0.5;

// Tints the albedo by the vertex color, through the tint mask when the material has one
void ApplyVertexTint(inout MaterialProperties_t mat, vec2 texCoord)
{
#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, GetUvSet(texCoord, g_bUseSecondaryUvForTintMask)).x);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif
}

// The color, opacity and vertex tint of the material
MaterialProperties_t InitGenericMaterial(vec2 texCoord, vec3 vertexNormals, vec4 color, bool scaleOpacityByVertexAlpha)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

#if (DIFFUSE_WRAP)
    mat.DiffuseWrapColor = g_vDiffuseWrapColor;
#endif

    mat.Albedo = color.rgb;

#if (TRANSLUCENT)
    const bool translucent = true;
#else
    const bool translucent = false;
#endif

    InitOpacity(mat, color.a, g_flOpacityScale, translucent, scaleOpacityByVertexAlpha, texCoord, g_flAlphaTestReference);

    ApplyVertexTint(mat, texCoord);

    return mat;
}

// The normal map with its strength applied, and the roughness
void DecodeGenericNormal(inout MaterialProperties_t mat, vec2 texCoord, vec4 normalTexture, float bumpStrength)
{
    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);

#if (F_ANISOTROPIC_GLOSS == 1)
    mat.RoughnessTex.xy = texture(g_tAnisoGloss, texCoord).rg;
#else
    mat.RoughnessTex.xy = normalTexture.bb;
#endif

    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, bumpStrength);
}

// The steps after the normal, metalness and ambient occlusion are set: the transmissive color, decal and glass
void FinishGenericMaterial(inout MaterialProperties_t mat, vec2 texCoord)
{
    ApplyTransmissiveColor(mat, texCoord);

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

#if (F_DECAL_TEXTURE == 1)
    mat.Albedo = ApplyDecalTexture(mat.Albedo, GetUvSet(texCoord, g_bUseSecondaryUvForDecal));
#endif

    ResolveDiffuseSpecular(mat, vec3(0.04));

#if (F_GLASS == 1)
    vec4 glassResult = GetGlassMaterial(mat, g_flOpacityScale);
    mat.Albedo = glassResult.rgb;
    mat.Opacity = glassResult.a;
#endif

    FinishMaterial(mat);
}

MaterialProperties_t GetGenericMaterial(vec2 texCoord, vec3 vertexNormals, vec4 color, vec4 normalTexture, float metalness, float ambientOcclusion, bool scaleOpacityByVertexAlpha, float bumpStrength, bool unlit)
{
    MaterialProperties_t mat = InitGenericMaterial(texCoord, vertexNormals, color, scaleOpacityByVertexAlpha);

    if (unlit)
    {
        FinishUnlitMaterial(mat);
        return mat;
    }

    DecodeGenericNormal(mat, texCoord, normalTexture, bumpStrength);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Metalness = metalness;
    mat.AmbientOcclusion = ambientOcclusion;

    FinishGenericMaterial(mat, texCoord);

    return mat;
}

#endif
