#version 460

// Fragment shader body of generic. The entry file includes its features file first, then sets this knob and includes this:
// GENERIC_REFLECTANCE_RANGE is 1 when the specular reads g_vReflectanceRange, otherwise the reflectance is fixed.

#define UNLIT (F_UNLIT == 1)

// Indirect and direct specular light are both on or both off
#if (F_SPECULAR == 1)
    #define IBL 1
#else
    #define IBL 0
#endif

#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "common/surface_base_ps.slang"
#include "common/surface_material.slang"
#include "common/surface_legacy_material.slang"
#include "common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

uniform float g_flOpacityScale = 1.0;
uniform float g_flBumpStrength = 1.0;

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMask;
#endif

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumMask;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec3 g_vSelfIllumTint = vec3(1.0);
#endif

#if (F_SPECULAR == 1)
    uniform sampler2D g_tRoughness;
    uniform sampler2D g_tMetalnessReflectanceFresnel;
    uniform vec2 g_vGlossinessRange;
    uniform vec2 g_vMetalnessRange;

    #if (GENERIC_REFLECTANCE_RANGE == 1)
        uniform vec2 g_vReflectanceRange;
    #endif
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    InitOpacity(mat, color.a, g_flOpacityScale, F_TRANSLUCENT == 1, false, texCoord, g_flAlphaTestReference);

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).y);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif

#if (F_SELF_ILLUM == 1)
    // Generic glows with its albedo instead of its lighting where the mask is. CS2 glows with the untinted color
    float illumMask = texture(g_tSelfIllumMask, texCoord).g;
    ApplyAlbedoSelfIllum(mat, illumMask, g_flSelfIllumScale, g_vSelfIllumTint);
#endif

#if (UNLIT)
    FinishUnlitMaterial(mat);
    return mat;
#endif

    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), g_flBumpStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    vec3 F0 = vec3(0.04);

#if (F_SPECULAR == 1)
    vec4 roughnessTexel = texture(g_tRoughness, texCoord);
    mat.RoughnessTex = vec2(GetRoughnessFromGlossinessRange(roughnessTexel, g_vGlossinessRange));

    vec4 metalnessReflectance = texture(g_tMetalnessReflectanceFresnel, texCoord);
    mat.Metalness = RemapByRange(metalnessReflectance.r, g_vMetalnessRange);

    #if (GENERIC_REFLECTANCE_RANGE == 1)
        F0 = vec3(RemapByRange(metalnessReflectance.g, g_vReflectanceRange));
    #endif
#else
    mat.RoughnessTex = vec2(1.0);
#endif

    FinishLitMaterial(mat, texCoord, F0);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = IBL != 0;

#if (UNLIT)
    RenderUnlitSurface(g_tColor, options);
#else
    RenderSurface(g_tColor, options);
#endif
}
