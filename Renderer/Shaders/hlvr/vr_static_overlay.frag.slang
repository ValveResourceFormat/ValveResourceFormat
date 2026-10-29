#version 460

#include "vr_static_overlay_features.slang"

// The blend mode 2 cuts out by the alpha
#define ALPHA_TEST_BY_BLEND_MODE (F_BLEND_MODE == 2)

#define LIGHTMAP_VR_SHADING 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

uniform float g_flOpacityScale = 1.0;
uniform float g_flMetalness = 0.0;

#if (F_LIT == 1)
    uniform sampler2D g_tNormal; // Sampler(UserConfig)
#endif

#if (F_BLEND_MODE == 6)
    #include "../features/self_illum_ps.slang"
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);

#if (F_BLEND_MODE == 3)
    color.rgb = SrgbLinearToGamma(color.rgb);
#endif

    mat.Albedo = color.rgb;

    float alpha = color.a * vVertexColorOut.a;
    InitOpacity(mat, alpha, g_flOpacityScale, IsBlendModeTranslucent(F_BLEND_MODE), false, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;

#if (F_BLEND_MODE == 6)
    mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);
#endif

#if (F_LIT == 0)
    mat.Albedo *= 1.0 - g_flMetalness;

    FinishUnlitMaterial(mat);
#else
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.NormalMap = ApplyNormalMapStrength(DecodeHemiOctahedronNormal(normalTexture.rg), 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.RoughnessTex = normalTexture.bb;
    mat.Metalness = g_flMetalness;

    FinishLitMaterial(mat, texCoord, vec3(0.04));
#endif

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendMode = F_BLEND_MODE;
    options.OccludeDirectDiffuse = false;

#if (F_LIT == 0)
    RenderUnlitSurface(g_tColor, options);
#else
    RenderSurface(g_tColor, options);
#endif
}
