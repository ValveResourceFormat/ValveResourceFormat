#version 460

#include "environment_parallax_layered_features.slang"

#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#define SCREEN_SPACE_AO 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_environment_layers.slang"

// Layer 1 is g_tColor and g_tNormalRoughness, the offsets are in 1024ths of a texture
uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalRoughness; // Sampler(UserConfig)
uniform sampler2D g_tMask;
uniform mat4 g_mAlbedoColorCorrect = mat4(1.0);
uniform float g_fVertexColorStrength1 = 1.0;

// Layer 2
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tMask2;
uniform sampler2D g_tNormalRoughness2;
uniform sampler2D g_tReflectionOcclusionMask;
uniform mat4 g_mAlbedoColorCorrect2 = mat4(1.0);
uniform float g_fVertexColorStrength2 = 1.0;
uniform float g_bLayer2ShadingBlendStrength = 1.0;
uniform float g_flLayer2Offset = 0.0;
uniform float g_flParallaxViewAngleLimit2 = 0.5;
uniform vec3 g_vEmissiveTint2 = vec3(1.0); // SrgbRead(true)
uniform float g_flEmissiveLevel2 = 0.0;
uniform bool g_bLayer2UseSecondaryUV = false;

uniform float g_flReflectionOcclusionOffset = 0.0;
uniform float g_flReflectionOcclusionViewAngleLimit = 0.5;
uniform bool g_bReflectionOcclusionUseSecondaryUV = false;

// Layer 3
uniform sampler2D g_tColor3; // SrgbRead(true)
uniform mat4 g_mAlbedoColorCorrect3 = mat4(1.0);
uniform float g_fVertexColorStrength3 = 1.0;
uniform vec3 g_vEmissiveTint3 = vec3(1.0); // SrgbRead(true)
uniform float g_flEmissiveLevel3 = 0.0;
uniform float g_flLayer3Offset = 0.0;
uniform float g_flParallaxViewAngleLimit3 = 0.5;
uniform bool g_bLayer3UseSecondaryUV = false;

#if (F_WORLD_OVERLAY == 1)
    uniform sampler2D g_tColorWorldOverlay; // SrgbRead(true)
    uniform float g_flWorldOverlayTiling = 512.0;
    uniform float g_flWorldOverlayBrightnessContrast = 1.0;
    uniform float g_flWorldOverlayDarknessContrast = 1.0;
    uniform float g_flWorldOverlayStrength1 = 1.0;
#endif

#if (F_WORLD_OVERLAY == 1)

// Projected by the interpolated vertex normal, not the normal mapped one
vec3 GetWorldOverlayFactor()
{
    vec4 overlay = SampleBiPlanar(g_tColorWorldOverlay, vFragPosition, normalize(vNormalOut), g_flWorldOverlayTiling);
    return GetOverlayFactor(overlay.rgb, g_flWorldOverlayDarknessContrast, g_flWorldOverlayBrightnessContrast);
}

#endif

vec3 GetLayerAlbedo(vec3 color, mat4 albedoColorCorrect, float vertexColorStrength)
{
    vec3 corrected = (vec4(color, 1.0) * albedoColorCorrect).rgb;
    return mix(corrected, corrected * vVertexColorOut.rgb, vertexColorStrength);
}

uniform float g_flAlphaTestReference = 0.5;

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    uniform float g_flOverrideBloomAmount1 = 1.0;
#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    // Parallax
    float invDepth = 1.0 / dot(mat.ViewDir, mat.GeometricNormal);
    float layer2Depth = (g_flLayer2Offset / 1024.0) * invDepth;
    float layer3Depth = (g_flLayer3Offset / 1024.0) * invDepth;
    float reflectionOcclusionDepth = (g_flReflectionOcclusionOffset / 1024.0) * invDepth;

    vec2 layer2Limit = vec2(g_flParallaxViewAngleLimit2);
    vec2 layer3Limit = vec2(g_flParallaxViewAngleLimit3);
    vec2 reflectionOcclusionLimit = vec2(g_flReflectionOcclusionViewAngleLimit);

    LayerMapping_t uv2 = GetDerivedMapping(vTexCoord2, mat.GeometricNormal);

    vec2 viewDirTs = vec2(dot(mat.ViewDir, mat.Tangent), dot(mat.ViewDir, mat.Bitangent));
    vec2 viewDirTs2 = vec2(dot(mat.ViewDir, uv2.Tangent), dot(mat.ViewDir, uv2.Bitangent));

    // Each layer on UV2 clamps the view direction the previous one on UV2 already clamped
    vec2 layer2UV;
    if (g_bLayer2UseSecondaryUV)
    {
        viewDirTs2 = clamp(viewDirTs2, -layer2Limit, layer2Limit);
        layer2UV = vTexCoord2 - viewDirTs2 * layer2Depth;
    }
    else
    {
        layer2UV = texCoord - clamp(viewDirTs, -layer2Limit, layer2Limit) * layer2Depth;
    }

    vec2 layer3UV;
    if (g_bLayer3UseSecondaryUV)
    {
        viewDirTs2 = clamp(viewDirTs2, -layer3Limit, layer3Limit);
        layer3UV = vTexCoord2 - viewDirTs2 * layer3Depth;
    }
    else
    {
        layer3UV = texCoord - clamp(viewDirTs, -layer3Limit, layer3Limit) * layer3Depth;
    }

    vec2 reflectionOcclusionUV;
    if (g_bReflectionOcclusionUseSecondaryUV)
    {
        reflectionOcclusionUV = vTexCoord2 - clamp(viewDirTs2, -reflectionOcclusionLimit, reflectionOcclusionLimit) * reflectionOcclusionDepth;
    }
    else
    {
        // Bounded above by the layer 3 limit rather than its own
        reflectionOcclusionUV = texCoord - max(min(viewDirTs, layer3Limit), -reflectionOcclusionLimit) * reflectionOcclusionDepth;
    }

    // Albedo
    vec4 color1 = texture(g_tColor, texCoord);
    vec4 color2 = texture(g_tColor2, layer2UV);
    vec4 color3 = texture(g_tColor3, layer3UV);
    float mask1 = texture(g_tMask, texCoord).r;
    float mask2 = texture(g_tMask2, layer2UV).r;

    vec3 albedo1 = GetLayerAlbedo(color1.rgb, g_mAlbedoColorCorrect, g_fVertexColorStrength1);
#if (F_WORLD_OVERLAY == 1)
    // Only the first layer takes the overlay
    albedo1 = mix(albedo1, albedo1 * GetWorldOverlayFactor(), g_flWorldOverlayStrength1);
#endif
    vec3 albedo2 = GetLayerAlbedo(color2.rgb, g_mAlbedoColorCorrect2, g_fVertexColorStrength2);
    vec3 albedo3 = GetLayerAlbedo(color3.rgb, g_mAlbedoColorCorrect3, g_fVertexColorStrength3);
    vec3 albedo = mix(mix(albedo3, albedo2, mask2), albedo1, mask1);

    // Self illumination, masked by the color alpha of layers 2 and 3
    float behindLayer1 = 1.0 - mask1;
    float behindLayer2 = 1.0 - mask2;
    float layer3Visibility = behindLayer1 * behindLayer2;

    vec3 emissiveTint2 = exp2(g_flEmissiveLevel2) * g_vEmissiveTint2;
    vec3 emissiveTint3 = exp2(g_flEmissiveLevel3) * g_vEmissiveTint3;
    float emissive2 = g_flEmissiveLevel2 * color2.a * behindLayer1 * GetLuma(emissiveTint2);
    float emissive3 = g_flEmissiveLevel3 * color3.a * behindLayer2 * GetLuma(emissiveTint3);

    // Glowing layers take the emissive tint into the albedo
    albedo = mix(albedo, emissiveTint2 * albedo, (1.0 - layer3Visibility) * saturate(emissive2));
    albedo = mix(albedo, emissiveTint3 * albedo, layer3Visibility * saturate(emissive3));
    float selfIllum = mix(behindLayer1 * emissive2, emissive3, layer3Visibility);

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    mat.BloomAmount = saturate(g_flOverrideBloomAmount1 * selfIllum * BLOOM_AMOUNT_TO_STORED);
#endif

    mat.Albedo = albedo;
    mat.IllumColor = albedo * selfIllum;
    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, g_flAlphaTestReference);

    // Normal and roughness, where the reflection occlusion mask makes layers fully rough
    vec4 normalTexture1 = texture(g_tNormalRoughness, texCoord);
    vec4 normalTexture2 = texture(g_tNormalRoughness2, layer2UV);
    float reflective = step(texture(g_tReflectionOcclusionMask, reflectionOcclusionUV).r, 0.5);

    float occludedRoughness1 = mix(1.0, normalTexture1.b, reflective);
    normalTexture1.b = mix(occludedRoughness1, normalTexture1.b, g_bLayer2ShadingBlendStrength);
    normalTexture2.b = mix(1.0, normalTexture2.b, reflective);
    vec4 normalTexture = mix(normalTexture1, normalTexture2, behindLayer1 * g_bLayer2ShadingBlendStrength);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    ResolveDiffuseSpecularCitadel(mat);
    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
