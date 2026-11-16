#version 460

// No specular light, the sticker adds its own reflection
#define IBL 0
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#define F_HOLOGRAM_LOOKUP_TEXTURE 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

#if (S_MODE_DEPTH == 0)
    #include "global_light.slang"
#endif

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform samplerCube g_tCubeMapReflect; // SrgbRead(true)
uniform sampler2D g_tEffectMask; // SrgbRead(true)

uniform sampler2D g_tBackgroundEffectPattern; // SrgbRead(true)
uniform sampler2D g_tBackgroundEffectNoise; // SrgbRead(true)
uniform sampler2D g_tBackgroundEffectNormal;
uniform sampler2D g_tAccentEffectPattern; // SrgbRead(true)
uniform sampler2D g_tAccentEffectNoise; // SrgbRead(true)
uniform sampler2D g_tAccentEffectNormal;

#if (F_HOLOGRAM_LOOKUP_TEXTURE == 1)
    uniform sampler2D g_tBackgroundHologramLookup; // SrgbRead(true)
    uniform sampler2D g_tAccentHologramLookup; // SrgbRead(true)

    uniform float g_flBackgroundHologramStrength = 0.6;
    uniform float g_flBackgroundHologramPatternOffset = 0.5;
    uniform float g_flBackgroundHologramNoiseOffset = 2.0;
    uniform float g_flAccentHologramStrength = 0.6;
    uniform float g_flAccentHologramPatternOffset = 0.5;
    uniform float g_flAccentHologramNoiseOffset = 2.0;
#endif

uniform float g_flNormalStrength = 1.0;
uniform float g_flSpecularIntensity = 1.0;
uniform float g_flDiffuseLightingStrength = 0.75;
uniform float g_flMipSharpness = 1.0;
uniform float g_flCubeMapStrength = 1.0;

uniform vec4 g_vBackgroundEffectMask = vec4(1.0);
uniform float g_flBackgroundBlendSharpness = 40.0;
uniform float g_flBackgroundColorAmount = 1.0;
uniform float g_flBackgroundEffectPatternParallax;
uniform float g_flBackgroundEffectMaskWithPattern;
uniform float g_flBackgroundEffectMaskWithNoise;
uniform float g_flBackgroundEffectNormalTiling = 1.0;
uniform float g_flBackgroundEffectNormalStrength = 0.05;
uniform float g_flBackgroundCubeMapEffectStrength = 10.0;
uniform vec4 g_vBackgroundEffectCubeMapTint = vec4(1.0);
uniform float g_flBackgroundEffectCubemapDiffuseTint = 0.0;

uniform vec4 g_vAccentEffectMask = vec4(1.0);
uniform float g_flAccentEffectPatternParallax;
uniform float g_flAccentEffectMaskWithPattern;
uniform float g_flAccentEffectMaskByLuminance = 1.0;
uniform float g_flAccentEffectMaskWithNoise;
uniform float g_flAccentEffectNormalTiling = 1.0;
uniform float g_flAccentEffectNormalStrength = 0.05;
uniform float g_flAccentCubeMapEffectStrength = 10.0;
uniform vec4 g_vAccentEffectCubeMapTint = vec4(1.0);

uniform float g_flAlphaTestReference = 0.5;

#if (S_MODE_DEPTH == 0)
// Offset of the view ray in tangent space, which shifts the effect patterns
vec2 GetStickerParallaxOffset(MaterialProperties_t mat)
{
    vec3 viewTangent = -vec3(dot(mat.ViewDir, mat.Tangent), dot(mat.ViewDir, mat.Bitangent), dot(mat.ViewDir, mat.GeometricNormal));
    return viewTangent.xy / (0.4 - viewTangent.z);
}

// Sharpens a layer mask by its texel, or flattens it when amount is negative
float MaskWithTexel(float texel, float amount)
{
    return mix(1.0 - texel, texel, amount * 0.5 + 0.5) * 2.0;
}

// The input scaled by the pattern and noise masks of one effect layer
float GetLayerEffect(float layerInput, float pattern, float noise, float patternAmount, float noiseAmount)
{
    return layerInput * MaskWithTexel(pattern, patternAmount) * MaskWithTexel(noise, noiseAmount);
}

// The color with the hue of hueSource, keeping its own saturation and value
vec3 ReplaceHue(vec3 color, vec3 hueSource)
{
    vec3 hsv = RgbToHsv(color);
    hsv.x = RgbToHsv(hueSource).x;

    return HsvToRgb(hsv);
}

// Reflection of the cube map, with the effect layers bending its direction and tinting it.
// accentMask returns the share of the accent layer, which the albedo is cut by.
vec3 GetStickerReflection(MaterialProperties_t mat, vec3 baseColor, vec3 backgroundAlbedo, float backgroundMask, vec4 effectMask, vec2 texCoord, out float accentMask)
{
    vec2 parallax = GetStickerParallaxOffset(mat);

    float backgroundPattern = texture(g_tBackgroundEffectPattern, texCoord + parallax * g_flBackgroundEffectPatternParallax).x * backgroundMask;
    float backgroundNoise = texture(g_tBackgroundEffectNoise, texCoord).x * backgroundMask;
    float backgroundEffect = GetLayerEffect(backgroundMask, backgroundPattern, backgroundNoise,
        g_flBackgroundEffectMaskWithPattern, g_flBackgroundEffectMaskWithNoise);

    float accentLuminance = GetLuma(backgroundAlbedo);
    float accentInput = dot(g_vAccentEffectMask, effectMask) * mix(1.0, accentLuminance, g_flAccentEffectMaskByLuminance);
    float accentPattern = texture(g_tAccentEffectPattern, texCoord + parallax * g_flAccentEffectPatternParallax).x * accentInput;
    float accentNoise = texture(g_tAccentEffectNoise, texCoord).x * accentInput;

    accentMask = GetLayerEffect(accentInput, accentPattern, accentNoise,
        g_flAccentEffectMaskWithPattern, g_flAccentEffectMaskWithNoise);

    float effect = saturate(backgroundEffect + accentMask);

    vec3 backgroundNormal = ScaleNormalMapSlope(
        DecodeDxt5Normal(texture(g_tBackgroundEffectNormal, texCoord * g_flBackgroundEffectNormalTiling, -2.0)),
        g_flBackgroundEffectNormalStrength);
    vec3 accentNormal = ScaleNormalMapSlope(
        DecodeDxt5Normal(texture(g_tAccentEffectNormal, texCoord * g_flAccentEffectNormalTiling, -2.0)),
        g_flAccentEffectNormalStrength);

    vec3 effectNormal = mix(backgroundNormal, accentNormal, accentMask);
    vec3 tangentNormal = normalize(vec3(effectNormal.xy + mat.NormalMap.xy, effectNormal.z));
    vec3 effectWorldNormal = CalculateWorldNormal(tangentNormal, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    vec3 reflectionDir = mix(reflect(mat.ViewDir, mat.Normal), reflect(mat.ViewDir, effectWorldNormal), effect);
    vec3 reflection = texture(g_tCubeMapReflect, reflectionDir).rgb;

    // Thin bright line along the horizon of the reflection
    float horizon = reflectionDir.z - reflectionDir.y / 3.0;
    reflection += saturate(horizon * -400.0) * pow(saturate(1.0 - abs(horizon)), 10.0) * 0.25;

    float NdotV = max(0.0, dot(mat.ViewDir, mat.Normal));

#if (F_HOLOGRAM_LOOKUP_TEXTURE == 1)
    float patternOffset = mix(g_flBackgroundHologramPatternOffset, g_flAccentHologramPatternOffset, accentMask);
    float noiseOffset = mix(g_flBackgroundHologramNoiseOffset, g_flAccentHologramNoiseOffset, accentMask);
    float lookupX = pow(1.0 - max(0.0, dot(mat.ViewDir, effectWorldNormal)), 0.5) * 20.0
        + mix(backgroundPattern, accentPattern, accentMask) * patternOffset
        + mix(backgroundNoise, accentNoise, accentMask) * noiseOffset;
    vec2 lookupCoord = vec2(fract(lookupX), 0.5);

    vec3 lookupColor = mix(texture(g_tBackgroundHologramLookup, lookupCoord).rgb, texture(g_tAccentHologramLookup, lookupCoord).rgb, accentMask);
    float hologramStrength = effect * mix(g_flBackgroundHologramStrength, g_flAccentHologramStrength, accentMask) * NdotV;

    reflection = mix(reflection, ReplaceHue(reflection, lookupColor), hologramStrength);
#endif

    float facingScale = mix(0.25, 1.0, 1.0 - NdotV);
    float effectStrength = mix(g_flBackgroundCubeMapEffectStrength, g_flAccentCubeMapEffectStrength, accentMask);
    float cubeStrength = mix(g_flCubeMapStrength, effectStrength, effect);
    vec3 effectTint = mix(g_vBackgroundEffectCubeMapTint.rgb, g_vAccentEffectCubeMapTint.rgb, accentMask);
    vec3 diffuseTint = mix(vec3(1.0), baseColor, g_flBackgroundEffectCubemapDiffuseTint);

    return reflection * facingScale * cubeStrength * mix(vec3(1.0), effectTint, effect) * diffuseTint;
}

// The Dota global light on the albedo, the highlight of the entity specular light and the reflection
LightingTerms_t ShadeSticker(MaterialProperties_t mat)
{
    float sunShadow;
    vec3 light = CalculateDotaGlobalLight(mat.Normal, mat.PositionWS, sunShadow);

    float highlight = pow(saturate(dot(g_vSpecularDirection_Power.xyz, reflect(mat.ViewDir, mat.Normal))), 1000.0) * g_flSpecularIntensity;
    vec3 highlightColor = g_vSpecularAlbedoColor.rgb * highlight;

    LightingTerms_t lighting = InitLighting();
    lighting.DiffuseDirect = mat.Albedo * light * g_flDiffuseLightingStrength + highlightColor + mat.IllumColor;

    return lighting;
}
#endif

float GetStickerBackgroundMask(vec4 effectMask)
{
    return saturate(dot(g_vBackgroundEffectMask, effectMask) * g_flBackgroundBlendSharpness);
}

// Where its mask is set, the background effect scales the color by the distance from the middle of the sticker
vec3 ApplyStickerBackground(vec3 color, float backgroundMask, vec2 texCoord)
{
    float radialFalloff = length(texCoord - vec2(0.5)) * 1.5;
    vec3 backgroundColor = color * g_flBackgroundColorAmount * radialFalloff;

    return mix(color, backgroundColor, backgroundMask);
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord, -g_flMipSharpness);
    vec4 effectMask = texture(g_tEffectMask, texCoord, -g_flMipSharpness);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    InitOpacity(mat, color.a, 1.0, false, false, texCoord, g_flAlphaTestReference);

    float backgroundMask = GetStickerBackgroundMask(effectMask);
    mat.Albedo = ApplyStickerBackground(color.rgb, backgroundMask, texCoord);

    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), g_flNormalStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (S_MODE_DEPTH == 0)
    float accentMask;
    mat.IllumColor = GetStickerReflection(mat, color.rgb, mat.Albedo, backgroundMask, effectMask, texCoord, accentMask);
    mat.Albedo *= 1.0 - accentMask;
#endif

    return mat;
}

void main()
{
#if (S_MODE_DEPTH == 0)
    SurfaceOptions_t options = DefaultSurfaceOptions();

    MaterialProperties_t mat = GetShadedMaterial();

    LightingTerms_t lighting = ShadeSticker(mat);
    outputColor = vec4(lighting.DiffuseDirect, mat.Opacity);

    FinishSurface(mat, lighting, options, g_tColor);
#else
    RenderDepthPass();
#endif
}
