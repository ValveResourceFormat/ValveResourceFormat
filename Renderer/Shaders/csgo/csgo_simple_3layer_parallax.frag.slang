#version 460

#include "csgo_simple_3layer_parallax_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tAmbientOcclusion;
uniform sampler2D g_tLayer0Mask; // Sampler(UserConfig)

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

uniform float g_flMetalness = 0.0;

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    uniform sampler2D g_tTransmissiveMask; // SrgbRead(true) Sampler(UserConfig)
#endif

// Layer 1
uniform sampler2D g_tLayer1Color; // SrgbRead(true) Sampler(UserConfig)
uniform vec3 g_vLayer1EmissiveTint = vec3(1.0);
uniform float g_flLayer1EmissiveLevel = 0.0;
uniform float g_flLayer1RefractionAmount = 0.0;
uniform float g_flLayer1RoughnessBlurMin = 0.0;
uniform float g_flLayer1RoughnessBlurMax = 0.0;
uniform float g_flLayer1Offset = 0.0;

// Layer 2, the offsets are in 1024ths of a texture
uniform sampler2D g_tLayer2Color; // SrgbRead(true) Sampler(UserConfig)
uniform vec3 g_vLayer2EmissiveTint = vec3(1.0);
uniform float g_flLayer2EmissiveLevel = 0.0;
uniform float g_flLayer2EmissiveFresnel = 0.0;
uniform float g_flLayer2RefractionAmount = 0.0;
uniform float g_flLayer2RoughnessBlurMin = 0.0;
uniform float g_flLayer2RoughnessBlurMax = 0.0;
uniform float g_flLayer2Offset = 0.0;

#if (F_SECOND_LAYER_CUBEMAP == 1)
    uniform samplerCube g_tLayer2Cubemap; // SrgbRead(true)
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    // The normal map refracts the layer UVs, so it is decoded first
    vec4 normalTexture = texture(g_tNormal, texCoord);
    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);

    // Parallax
    vec3 viewDirInv = -mat.ViewDir;
    float invDepth = 1.0 / dot(viewDirInv, mat.GeometricNormal);
    float NdotV = clamp(dot(mat.ViewDir, mat.GeometricNormal), 0.0001, 1.0);
    float fresnel = saturate(pow(NdotV, 0.7));
    vec2 viewDirTs = vec2(dot(viewDirInv, mat.Tangent), dot(viewDirInv, mat.Bitangent));
    vec2 normalXY = vec2(mat.NormalMap.x, -mat.NormalMap.y);

    // Shader has "Expression(this/1024)", we do this here
    vec2 parallaxDir = viewDirTs * (fresnel * invDepth);
    vec2 layer1UV = normalXY * g_flLayer1RefractionAmount + texCoord + parallaxDir * (g_flLayer1Offset / 1024.0);
    vec2 layer2UV = normalXY * g_flLayer2RefractionAmount + texCoord + parallaxDir * (g_flLayer2Offset / 1024.0);

    // Rougher surfaces blur the layers below them
    float layer1Blur = mix(g_flLayer1RoughnessBlurMin, g_flLayer1RoughnessBlurMax, normalTexture.b);
    float layer2Blur = mix(g_flLayer2RoughnessBlurMin, g_flLayer2RoughnessBlurMax, normalTexture.b);

    vec4 color = texture(g_tColor, texCoord);
    float mask = texture(g_tLayer0Mask, texCoord).x;
    vec4 layer1Color = texture(g_tLayer1Color, layer1UV, layer1Blur);
    vec3 layer2Color = texture(g_tLayer2Color, layer2UV, layer2Blur).rgb;

    color.rgb = mix(mix(layer2Color, layer1Color.rgb, layer1Color.a), color.rgb, mask);

    // Tints the emissive layers too
#if (F_TINT_MASK == 1)
    float tintMask = texture(g_tTintMask, texCoord).x;
    vec3 tintFactor = mix(vec3(1.0), vVertexColorOut.rgb, tintMask);
#else
    vec3 tintFactor = vVertexColorOut.rgb;
#endif
    color.rgb *= tintFactor;

#if (F_METALNESS_TEXTURE == 1)
    mat.Metalness = color.a * mask;
#else
    mat.Metalness = g_flMetalness * mask;
#endif

    // Emissive
    vec3 layer1Emissive = (layer1Color.rgb * g_vLayer1EmissiveTint * g_flLayer1EmissiveLevel) * tintFactor;
    float emissiveFresnel = saturate(pow(NdotV, g_flLayer2EmissiveFresnel));

#if (F_SECOND_LAYER_CUBEMAP == 1)
    // The cubemap is looked up along the view ray, offset by the tangent space normal as if it were in world space
    vec3 cubemapDirection = viewDirInv + vec3(normalXY * g_flLayer2RefractionAmount, 0.0);
    vec3 layer2EmissiveColor = textureLod(g_tLayer2Cubemap, cubemapDirection, layer2Blur).rgb * layer2Color;
#else
    vec3 layer2EmissiveColor = layer2Color;
#endif

    vec3 layer2Emissive = (layer2EmissiveColor * emissiveFresnel * g_vLayer2EmissiveTint * g_flLayer2EmissiveLevel) * tintFactor;
    mat.IllumColor = mix(layer2Emissive, layer1Emissive, layer1Color.a) * (1.0 - mask);

    mat.Albedo = color.rgb;
    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, g_flAlphaTestReference);

    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    mat.TransmissiveColor = mat.Albedo * texture(g_tTransmissiveMask, texCoord).r;
#endif

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    // Ambient occlusion darkens indirect light and specular, direct diffuse light stays unoccluded
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColor, options);
}
