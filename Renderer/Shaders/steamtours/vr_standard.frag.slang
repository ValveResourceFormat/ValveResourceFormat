#version 460

#include "vr_standard_features.slang"

#define IBL (F_SPECULAR_CUBE_MAP == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../features/blend_second_layer.slang"

uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#if (F_HIGH_QUALITY_GLOSS == 1)
    uniform sampler2D g_tNormal2;
    uniform sampler2D g_tGloss;
#endif

#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
#elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
    uniform sampler2D g_tColor1; // SrgbRead(true)
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
#endif

uniform float g_flOpacityScale = 1.0;
uniform float g_flAlphaTestReference = 0.5;

#if (F_BLEND > 0)
    in vec4 vColorBlendValues;

    uniform vec2 g_vLayer1Scale = vec2(1.0);

    #if (F_SPECULAR == 1)
        uniform sampler2D g_tLayer1Color; // SrgbRead(true)
    #else
        uniform sampler2D g_tLayer1Color2; // SrgbRead(true)
    #endif

    uniform sampler2D g_tLayer1RevealMask;
    uniform float g_flLayer1BlendSoftness = 0.5;

    #if (F_BLEND_NORMALS == 1)
        uniform sampler2D g_tLayer1Normal;
    #endif
#endif

#if (F_METALNESS_TEXTURE == 1)
    uniform sampler2D g_tMetalnessReflectance;
    uniform vec2 g_vMetalnessRange = vec2(0.0, 1.0);
#else
    uniform float g_flMetalness = 0.0;

    #if (F_SPECULAR == 1 && (TRANSLUCENT || (F_ALPHA_TEST == 1)))
        uniform sampler2D g_tMetalnessReflectance2;
    #endif
#endif

uniform vec2 g_vReflectanceRange = vec2(0.0, 1.0);

#if (F_SPECULAR_CUBE_MAP == 1)
    uniform float g_flCubeMapBrightness = 1.0;
#endif

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumMask; // SrgbRead(true)
    uniform float g_flSelfIllumAlbedoFactor = 0.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
#endif

float RemapToRange(float value, vec2 range)
{
    return mix(range.x, max(range.x, range.y), value);
}

vec4 SampleColor(vec2 texCoord)
{
#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    return texture(g_tColor, texCoord);
#elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
    return texture(g_tColor1, texCoord);
#else
    return texture(g_tColor2, texCoord);
#endif
}

#if (F_BLEND > 0)
// Mixes the second layer over the color and the normal texture by the vertex paint
void BlendSecondLayer(vec2 texCoord, inout vec4 color, inout vec4 normalTexture)
{
    Layer2_t layer2;
    layer2.TexCoord = texCoord * g_vLayer1Scale;
    layer2.NormalTexture = normalTexture;

    #if (F_SPECULAR == 1)
        layer2.Color = texture(g_tLayer1Color, layer2.TexCoord);
    #else
        layer2.Color = texture(g_tLayer1Color2, layer2.TexCoord);
    #endif

    #if (F_BLEND_NORMALS == 1)
        layer2.NormalTexture = texture(g_tLayer1Normal, layer2.TexCoord);
    #endif

    vec4 blendModTexel = texture(g_tLayer1RevealMask, layer2.TexCoord);
    float blendFactor = ApplyBlendModulation(vColorBlendValues.r, blendModTexel.g, blendModTexel.r * g_flLayer1BlendSoftness);

    BlendLayer2(color, normalTexture, layer2, blendFactor);
}
#endif

// Sets the metalness and returns the reflectance, which is the color texture's alpha unless the opacity needs it
float InitMetalnessReflectance(inout MaterialProperties_t mat, vec2 texCoord, float colorAlpha)
{
#if (F_METALNESS_TEXTURE == 1)
    vec2 metalnessReflectance = texture(g_tMetalnessReflectance, texCoord).rg;
    mat.Metalness = RemapToRange(metalnessReflectance.r, g_vMetalnessRange);
    return metalnessReflectance.g;
#else
    mat.Metalness = g_flMetalness;

    #if (F_SPECULAR == 1 && (TRANSLUCENT || (F_ALPHA_TEST == 1)))
        return texture(g_tMetalnessReflectance2, texCoord).r;
    #else
        return colorAlpha;
    #endif
#endif
}

void InitAlbedoAndOpacity(inout MaterialProperties_t mat, vec2 texCoord, vec4 color)
{
    mat.Albedo = color.rgb;

#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    mat.Opacity = color.a;
#endif

#if (TRANSLUCENT)
    mat.Opacity *= g_flOpacityScale;
#endif

#if (F_ALPHA_TEST == 1)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord, g_flAlphaTestReference);
#endif

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).x);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif
}

void InitSelfIllumination(inout MaterialProperties_t mat, vec2 texCoord)
{
#if (F_SELF_ILLUM == 1)
    vec3 illumMask = vec3(SampleSelfIllumMask(g_tSelfIllumMask, texCoord, g_vSelfIllumScrollSpeed).r);
    mat.IllumColor = GetSelfIllumination(illumMask, mat.Albedo, g_flSelfIllumAlbedoFactor, g_flSelfIllumBrightness, g_flSelfIllumScale, g_vSelfIllumTint);
#endif
}

// The high quality gloss shader reads its normal from a texture of its own and adds a gloss texture to the roughness
void InitNormalAndRoughness(inout MaterialProperties_t mat, vec2 texCoord, vec4 normalTexture)
{
#if (F_HIGH_QUALITY_GLOSS == 1)
    normalTexture = texture(g_tNormal2, texCoord);
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.ag);
    mat.RoughnessTex.xy = normalTexture.rb;

#if (F_HIGH_QUALITY_GLOSS == 1)
    mat.RoughnessTex.xy = min(mat.RoughnessTex.xy + texture(g_tGloss, texCoord).ag, vec2(1.0));
#endif

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
}

float GetReflectance(inout MaterialProperties_t mat, vec2 texCoord, float colorAlpha)
{
    float reflectance = InitMetalnessReflectance(mat, texCoord, colorAlpha);

#if (F_SPECULAR == 1)
    return RemapToRange(reflectance, g_vReflectanceRange);
#else
    return 0.0;
#endif
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = SampleColor(texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

#if (F_BLEND > 0)
    BlendSecondLayer(texCoord, color, normalTexture);
#endif

    InitAlbedoAndOpacity(mat, texCoord, color);
    InitSelfIllumination(mat, texCoord);

#if (F_UNLIT == 1)
    FinishUnlitMaterial(mat);
    return mat;
#endif

    InitNormalAndRoughness(mat, texCoord, normalTexture);
    float reflectance = GetReflectance(mat, texCoord, color.a);

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    // Reflectance is what takes energy from the diffuse, metalness only tints the specular
    mat.DiffuseColor = mat.Albedo - mat.Albedo * reflectance;
    mat.SpecularColor = reflectance * mix(vec3(1.0), mat.Albedo, mat.Metalness);

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR == 1;

#if (F_SPECULAR == 0)
    options.SpecularIndirectScale = 0.0;
#elif (F_SPECULAR_CUBE_MAP == 1)
    options.SpecularIndirectScale = g_flCubeMapBrightness * 1.1;
#endif

#if (F_BLEND > 0)
    options.BlendValues = vColorBlendValues.rgb;
#endif

#if (F_UNLIT == 1)
    RenderUnlitSurface(g_tNormal, options);
#else
    RenderSurface(g_tNormal, options);
#endif
}
