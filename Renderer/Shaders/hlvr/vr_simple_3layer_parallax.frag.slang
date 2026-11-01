#version 460

#include "vr_simple_3layer_parallax_features.slang"

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_parallax.slang"

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)
uniform sampler2D g_tColorB; // SrgbRead(true)
uniform sampler2D g_tColorC; // SrgbRead(true)
uniform sampler2D g_tMasks;
uniform sampler2D g_tEmissiveB; // SrgbRead(true)
uniform sampler2D g_tEmissiveC; // SrgbRead(true)

uniform float g_flMetalnessA = 0.0;
uniform float g_flMetalnessB = 0.0;
uniform float g_flMetalnessC = 0.0;
uniform float g_flMaterial0TintAmount = 1.0;
uniform float g_flMaterial1TintAmount = 1.0;
uniform float g_flMaterial2TintAmount = 1.0;
uniform float g_flMaterial1EmissiveLevel = 1.0;
uniform float g_flMaterial2EmissiveLevel = 1.0;
uniform float g_flFace0Displacement = 0.0;
uniform float g_flFace1Displacement = 0.0;
uniform float g_flFace2Displacement = 0.0;
uniform vec2 g_vTexCoordScale0 = vec2(1.0);
uniform vec2 g_vTexCoordScale1 = vec2(1.0);
uniform vec2 g_vTexCoordScale2 = vec2(1.0);
uniform vec2 g_vTexCoordOffset0 = vec2(0.0);
uniform vec2 g_vTexCoordOffset1 = vec2(0.0);
uniform vec2 g_vTexCoordOffset2 = vec2(0.0);

// The clamped combo samples every texture with clamped addressing, which ends at the outer texel centers
vec4 SampleLayerTexture(sampler2D tex, vec2 texCoord)
{
#if (F_CLAMP_UV_CHANNEL == 1)
    vec2 halfTexel = 0.5 / vec2(textureSize(tex, 0));
    texCoord = clamp(texCoord, halfTexel, 1.0 - halfTexel);
#endif
    return texture(tex, texCoord);
}

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec2 parallaxDir = GetParallaxDirection(mat);
    vec2 layer0UV = GetLayerTexCoord(texCoord, g_vTexCoordScale0, g_vTexCoordOffset0, g_flFace0Displacement, parallaxDir);
    vec2 layer1UV = GetLayerTexCoord(texCoord, g_vTexCoordScale1, g_vTexCoordOffset1, g_flFace1Displacement, parallaxDir);
    vec2 layer2UV = GetLayerTexCoord(texCoord, g_vTexCoordScale2, g_vTexCoordOffset2, g_flFace2Displacement, parallaxDir);

    vec4 layer0Color = SampleLayerTexture(g_tColorA, layer0UV);
    vec4 layer1Color = SampleLayerTexture(g_tColorB, layer1UV);
    vec3 layer2Color = SampleLayerTexture(g_tColorC, layer2UV).rgb;
    vec4 normalTexture = SampleLayerTexture(g_tNormalA, layer0UV);
    float mask = SampleLayerTexture(g_tMasks, layer0UV).r;
    float layer1Mask = layer1Color.a;

    vec3 layer0Tint = mix(vec3(1.0), vVertexColorOut.rgb, g_flMaterial0TintAmount);
    vec3 layer1Tint = mix(vec3(1.0), vVertexColorOut.rgb, g_flMaterial1TintAmount);
    vec3 layer2Tint = mix(vec3(1.0), vVertexColorOut.rgb, g_flMaterial2TintAmount);

    vec3 innerAlbedo = mix(layer2Color * layer2Tint, layer1Color.rgb * layer1Tint, layer1Mask);
    mat.Albedo = mix(innerAlbedo, layer0Color.rgb * layer0Tint, mask);
    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, g_flAlphaTestReference);

    float innerMetalness = mix(g_flMetalnessC, g_flMetalnessB, layer1Mask);
    mat.Metalness = mix(innerMetalness, g_flMetalnessA, mask);

    vec3 layer1Emissive = SampleLayerTexture(g_tEmissiveB, layer1UV).rgb * g_flMaterial1EmissiveLevel * layer1Tint;
    vec3 layer2Emissive = SampleLayerTexture(g_tEmissiveC, layer2UV).rgb * g_flMaterial2EmissiveLevel * layer2Tint;
    mat.IllumColor = mix(layer2Emissive, layer1Emissive, layer1Mask) * (1.0 - mask);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.AmbientOcclusion = layer0Color.a;

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    // Ambient occlusion darkens indirect light and specular, direct diffuse light stays unoccluded
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColorA, options);
}
