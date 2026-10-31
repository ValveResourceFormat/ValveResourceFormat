#version 460

#include "environment_blend_features.slang"

// Rect lights shade as area lights, the way Deadlock does it
#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// Deadlock blends the light probe volumes in every lightmap version
#define ENABLE_LPV_BLENDING 1

#define SCREEN_SPACE_AO 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_environment_layers.slang"

in vec2 vTexCoord2;
in vec3 vColorBlendValues;

#if (F_WORLD_OVERLAY_USE_OBJECT_SPACE == 1)
    in vec3 vPositionOs;
#endif

// Layer 1
#include "environment_layer1_uniforms.slang"

#if (F_LAYER1_BIPLANAR == 1)
    uniform float g_flBiPlanarTiling1 = 196.0;
#else
    uniform vec4 g_vScaleRotationTranslation1 = vec4(1.0, 0.0, 0.0, 0.0);
#endif

// Layer 2
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tNormalRoughness2;
uniform sampler2D g_tPacked2;
uniform sampler2D g_tRevealMask2;

uniform mat4 g_mAlbedoColorCorrect2 = mat4(1.0);
uniform mat4 g_mTextureColorTint2 = mat4(1.0);

uniform bool g_bMaskColorTint2 = true;
uniform bool g_bMaskVertexColorTint2 = true;
uniform bool g_bModelTint2 = true;
uniform float g_fVertexColorStrength2 = 1.0;
uniform float g_fLayerNormalStrength2 = 1.0;
uniform float g_fLayerSelfIllumScale2 = 1.0;
uniform vec3 g_vLayerSelfIllumTint2 = vec3(1.0);
uniform vec2 g_vLayerRoughnessContrastBrightness2 = vec2(1.0, 0.0);
uniform vec2 g_vLayerTintMaskContrastBrightness2 = vec2(1.0, 0.0);
uniform vec2 g_vLayerMetalnessContrastBrightness2 = vec2(1.0, 0.0);

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    uniform float g_flOverrideBloomAmount2 = 1.0;
#endif

uniform float g_flRevealOffset2 = 0.0;
uniform float g_flRevealSoftness2 = 0.125;
uniform bool g_bRevealInvert2 = false;
uniform bool g_bRevealUseSecondaryUv2 = false;
uniform vec4 g_vRevealScaleRotationTranslation2 = vec4(1.0, 0.0, 0.0, 0.0);

uniform bool g_bBorderEffects2 = false;
uniform vec2 g_vBorderWidth2 = vec2(0.125);
uniform vec2 g_vBorderSoftness2 = vec2(0.5);
uniform vec2 g_vBorderLayerAmount2 = vec2(1.0);
uniform int g_nBorderBlendMode2 = 0; // 0="Multiply", 1="Mod2x", 2="Blend"
uniform vec4 g_vBorderTint2 = vec4(0.5, 0.5, 0.5, 1.0);
uniform float g_flBorderRoughnessStrength2 = 0.0;
uniform float g_flBorderRoughness2 = 0.5;

#if (F_LAYER2_BIPLANAR == 1)
    uniform float g_flBiPlanarTiling2 = 196.0;
#else
    uniform vec4 g_vScaleRotationTranslation2 = vec4(1.0, 0.0, 0.0, 0.0);
#endif

// Layer 3
#if (F_LAYER_COUNT == 1)
    uniform sampler2D g_tColor3; // SrgbRead(true)
    uniform sampler2D g_tNormalRoughness3;
    uniform sampler2D g_tPacked3;
    uniform sampler2D g_tRevealMask3;

    uniform mat4 g_mAlbedoColorCorrect3 = mat4(1.0);
    uniform mat4 g_mTextureColorTint3 = mat4(1.0);

    uniform bool g_bMaskColorTint3 = true;
    uniform bool g_bMaskVertexColorTint3 = true;
    uniform bool g_bModelTint3 = true;
    uniform float g_fVertexColorStrength3 = 1.0;
    uniform float g_fLayerNormalStrength3 = 1.0;
    uniform float g_fLayerSelfIllumScale3 = 1.0;
    uniform vec3 g_vLayerSelfIllumTint3 = vec3(1.0);
    uniform vec2 g_vLayerRoughnessContrastBrightness3 = vec2(1.0, 0.0);
    uniform vec2 g_vLayerTintMaskContrastBrightness3 = vec2(1.0, 0.0);
    uniform vec2 g_vLayerMetalnessContrastBrightness3 = vec2(1.0, 0.0);

    uniform float g_flRevealOffset3 = 0.0;
    uniform float g_flRevealSoftness3 = 0.125;
    uniform bool g_bRevealInvert3 = false;
    uniform bool g_bRevealUseSecondaryUv3 = false;
    uniform vec4 g_vRevealScaleRotationTranslation3 = vec4(1.0, 0.0, 0.0, 0.0);

    uniform bool g_bBorderEffects3 = false;
    uniform vec2 g_vBorderWidth3 = vec2(0.125);
    uniform vec2 g_vBorderSoftness3 = vec2(0.5);
    uniform vec3 g_vBorderLayerAmount3 = vec3(1.0);
    uniform int g_nBorderBlendMode3 = 0;
    uniform vec4 g_vBorderTint3 = vec4(0.5, 0.5, 0.5, 1.0);
    uniform float g_flBorderRoughnessStrength3 = 0.0;
    uniform float g_flBorderRoughness3 = 0.5;

    #if (F_LAYER3_BIPLANAR == 1)
        uniform float g_flBiPlanarTiling3 = 196.0;
    #else
        uniform vec4 g_vScaleRotationTranslation3 = vec4(1.0, 0.0, 0.0, 0.0);
    #endif
#endif

// UV overlay: one texture laid over every layer, plus a mask that pushes layer 2 in on
// top of the vertex paint.
#if (F_UV_OVERLAY == 1)
    uniform sampler2D g_tColorOverlay; // SrgbRead(true)
    uniform sampler2D g_tNormalRoughnessOverlay;
    uniform sampler2D g_tRevealOverlay;

    uniform vec4 g_vOverlayScaleRotationTranslation = vec4(1.0, 0.0, 0.0, 0.0);
    uniform bool g_bUseSecondaryUvForOverlay = true;
    uniform float g_flOverlayDarknessContrast = 1.0;
    uniform float g_flOverlayBrightnessContrast = 1.0;

    uniform int g_nOverlayTintMask1 = 0; // 0="Unmasked", 1="Masked", 2="Inverted"
    uniform int g_nOverlayTintMask2 = 0;
    uniform float g_flOverlayStrength1 = 1.0;
    uniform float g_flOverlayStrength2 = 1.0;
    uniform float g_flOverlayNormalStrength1 = 1.0;
    uniform float g_flOverlayNormalStrength2 = 1.0;
    uniform float g_flOverlayRoughnessStrength1 = 0.0;
    uniform float g_flOverlayRoughnessStrength2 = 0.0;

    uniform bool g_bRevealOverlayInvert2 = false;
    uniform float g_flRevealOverlayStrength2 = 1.0;

    #if (F_LAYER_COUNT == 1)
        uniform int g_nOverlayTintMask3 = 0;
        uniform float g_flOverlayStrength3 = 1.0;
        uniform float g_flOverlayNormalStrength3 = 1.0;
        uniform float g_flOverlayRoughnessStrength3 = 0.0;
    #endif
#endif

// World overlay: biplanar-projected colour modulation, shared by every layer.
#if (F_WORLD_OVERLAY > 0)
    uniform sampler2D g_tColorWorldOverlay; // SrgbRead(true)

    uniform int g_nWorldOverlayTintMask1 = 1; // 0="Unmasked", 1="Masked", 2="Inverted"
    uniform int g_nWorldOverlayTintMask2 = 1;
    #if (F_LAYER_COUNT == 1)
        uniform int g_nWorldOverlayTintMask3 = 1;
    #endif

    #if (F_WORLD_OVERLAY == 2)
        uniform sampler2D g_tColorWorldOverlay2; // SrgbRead(true)

        uniform vec2 g_vWorldOverlayTiling = vec2(512.0);
        uniform vec2 g_vWorldOverlayBrightnessContrast = vec2(1.0);
        uniform vec2 g_vWorldOverlayDarknessContrast = vec2(1.0);
        uniform vec2 g_vWorldOverlayStrengths1 = vec2(1.0, 0.0);
        uniform vec2 g_vWorldOverlayStrengths2 = vec2(1.0, 0.0);
        #if (F_LAYER_COUNT == 1)
            uniform vec2 g_vWorldOverlayStrengths3 = vec2(1.0, 0.0);
        #endif
    #else
        uniform float g_flWorldOverlayTiling = 512.0;
        uniform float g_flWorldOverlayBrightnessContrast = 1.0;
        uniform float g_flWorldOverlayDarknessContrast = 1.0;
        uniform float g_flWorldOverlayStrength1 = 1.0;
        uniform float g_flWorldOverlayStrength2 = 1.0;
        #if (F_LAYER_COUNT == 1)
            uniform float g_flWorldOverlayStrength3 = 1.0;
        #endif
    #endif
#endif

// Every layer above the first is revealed by its own mask weighed against a channel of
// the vertex paint.
void GetPaintBlends(out float blend2, out float blend3)
{
    blend2 = vColorBlendValues.x;
    blend3 = 0.0;

#if (F_LAYER_COUNT == 1)
    blend3 = vColorBlendValues.y;
    blend2 = (blend3 < 1.0) ? blend2 : 0.0;
    blend3 = (blend3 > 1e-5) ? blend3 : 0.0;
#endif

    blend2 = (blend2 > 1e-5) ? blend2 : 0.0;
}

#if (F_UV_OVERLAY == 1)
vec2 GetOverlayTexCoord(vec2 texCoord)
{
    return ApplyUvTransform(
        g_bUseSecondaryUvForOverlay ? vTexCoord2 : texCoord,
        g_vOverlayScaleRotationTranslation);
}
#endif

float ApplyRevealOverlay(float blend2, vec2 texCoord)
{
#if (F_UV_OVERLAY == 1)
    float revealOverlay = texture(g_tRevealOverlay, GetOverlayTexCoord(texCoord)).r;
    revealOverlay = g_bRevealOverlayInvert2 ? (1.0 - revealOverlay) : revealOverlay;
    return saturate(blend2 + revealOverlay * g_flRevealOverlayStrength2);
#else
    return blend2;
#endif
}

Layer_t SampleLayer1(vec2 texCoord, vec3 geometricNormal, out vec3 illumColor)
{
#if (F_LAYER1_BIPLANAR == 1)
    LayerMapping_t mapping = GetBiPlanarMapping(geometricNormal, g_flBiPlanarTiling1);
#else
    LayerMapping_t mapping = GetMeshMapping(ApplyUvTransform(texCoord, g_vScaleRotationTranslation1));
#endif

    Layer_t layer = SampleLayer(g_tColor1, g_tNormalRoughness1, g_tPacked1, mapping,
        g_mAlbedoColorCorrect1, g_mTextureColorTint1,
        g_vLayerRoughnessContrastBrightness1, g_vLayerTintMaskContrastBrightness1, g_vLayerMetalnessContrastBrightness1,
        g_fLayerNormalStrength1, g_fLayerSelfIllumScale1,
        g_bMaskColorTint1, g_bMaskVertexColorTint1, g_bModelTint1, g_fVertexColorStrength1);

    // Self illumination is taken before any seam or overlay decoration touches the albedo.
    illumColor = layer.Albedo * layer.SelfIllum * g_vLayerSelfIllumTint1;
    return layer;
}

Layer_t SampleLayer2(vec2 texCoord, vec3 geometricNormal, out vec3 illumColor)
{
#if (F_LAYER2_BIPLANAR == 1)
    LayerMapping_t mapping = GetBiPlanarMapping(geometricNormal, g_flBiPlanarTiling2);
#else
    LayerMapping_t mapping = GetMeshMapping(ApplyUvTransform(texCoord, g_vScaleRotationTranslation2));
#endif

    Layer_t layer = SampleLayer(g_tColor2, g_tNormalRoughness2, g_tPacked2, mapping,
        g_mAlbedoColorCorrect2, g_mTextureColorTint2,
        g_vLayerRoughnessContrastBrightness2, g_vLayerTintMaskContrastBrightness2, g_vLayerMetalnessContrastBrightness2,
        g_fLayerNormalStrength2, g_fLayerSelfIllumScale2,
        g_bMaskColorTint2, g_bMaskVertexColorTint2, g_bModelTint2, g_fVertexColorStrength2);

    illumColor = layer.Albedo * layer.SelfIllum * g_vLayerSelfIllumTint2;
    return layer;
}

// Without a third layer this is an empty layer that never shows
Layer_t SampleLayer3(vec2 texCoord, vec3 geometricNormal, out vec3 illumColor)
{
    Layer_t layer = Layer_t(vec3(0.0), vec3(0.0), 0.0, 0.0, 0.0, 0.0);
    illumColor = vec3(0.0);

#if (F_LAYER_COUNT == 1)
    #if (F_LAYER3_BIPLANAR == 1)
        LayerMapping_t mapping = GetBiPlanarMapping(geometricNormal, g_flBiPlanarTiling3);
    #else
        LayerMapping_t mapping = GetMeshMapping(ApplyUvTransform(texCoord, g_vScaleRotationTranslation3));
    #endif

    layer = SampleLayer(g_tColor3, g_tNormalRoughness3, g_tPacked3, mapping,
        g_mAlbedoColorCorrect3, g_mTextureColorTint3,
        g_vLayerRoughnessContrastBrightness3, g_vLayerTintMaskContrastBrightness3, g_vLayerMetalnessContrastBrightness3,
        g_fLayerNormalStrength3, g_fLayerSelfIllumScale3,
        g_bMaskColorTint3, g_bMaskVertexColorTint3, g_bModelTint3, g_fVertexColorStrength3);

    illumColor = layer.Albedo * layer.SelfIllum * g_vLayerSelfIllumTint3;
#endif

    return layer;
}

float GetRevealThreshold2(vec2 texCoord)
{
    return SampleRevealThreshold(g_tRevealMask2, g_bRevealUseSecondaryUv2 ? vTexCoord2 : texCoord,
        g_vRevealScaleRotationTranslation2, g_bRevealInvert2, g_flRevealOffset2, g_flRevealSoftness2);
}

float GetRevealThreshold3(vec2 texCoord)
{
#if (F_LAYER_COUNT == 1)
    return SampleRevealThreshold(g_tRevealMask3, g_bRevealUseSecondaryUv3 ? vTexCoord2 : texCoord,
        g_vRevealScaleRotationTranslation3, g_bRevealInvert3, g_flRevealOffset3, g_flRevealSoftness3);
#else
    return 0.0;
#endif
}

// Each seam decorates the layers below it too, so a layer's border runs as soon as it is sampled.
void ApplyBorder2(inout Layer_t layer1, inout Layer_t layer2, float blend2, float threshold2)
{
    if (!g_bBorderEffects2 || blend2 <= 0.0)
    {
        return;
    }

    vec3 amount = GetBorderAmount(blend2, threshold2, g_vBorderWidth2, g_vBorderSoftness2, vec3(g_vBorderLayerAmount2, 0.0));

    ApplyBorderToLayer(layer1, g_vBorderTint2, g_nBorderBlendMode2, g_flBorderRoughness2, g_flBorderRoughnessStrength2, amount.x);
    ApplyBorderToLayer(layer2, g_vBorderTint2, g_nBorderBlendMode2, g_flBorderRoughness2, g_flBorderRoughnessStrength2, amount.y);
}

void ApplyBorder3(inout Layer_t layer1, inout Layer_t layer2, inout Layer_t layer3, float blend3, float threshold3)
{
#if (F_LAYER_COUNT == 1)
    if (!g_bBorderEffects3 || blend3 <= 0.0)
    {
        return;
    }

    vec3 amount = GetBorderAmount(blend3, threshold3, g_vBorderWidth3, g_vBorderSoftness3, g_vBorderLayerAmount3);

    ApplyBorderToLayer(layer1, g_vBorderTint3, g_nBorderBlendMode3, g_flBorderRoughness3, g_flBorderRoughnessStrength3, amount.x);
    ApplyBorderToLayer(layer2, g_vBorderTint3, g_nBorderBlendMode3, g_flBorderRoughness3, g_flBorderRoughnessStrength3, amount.y);
    ApplyBorderToLayer(layer3, g_vBorderTint3, g_nBorderBlendMode3, g_flBorderRoughness3, g_flBorderRoughnessStrength3, amount.z);
#endif
}

#if (F_WORLD_OVERLAY > 0)
vec3 GetWorldOverlayPosition()
{
#if (F_WORLD_OVERLAY_USE_OBJECT_SPACE == 1)
    return vPositionOs;
#else
    return vFragPosition;
#endif
}

void SampleWorldOverlay(int index, vec3 position, vec3 geometricNormal, out vec3 factor, out float strength1, out float strength2)
{
#if (F_WORLD_OVERLAY == 2)
    vec4 overlay = (index == 1)
        ? SampleBiPlanar(g_tColorWorldOverlay2, position, geometricNormal, g_vWorldOverlayTiling[index])
        : SampleBiPlanar(g_tColorWorldOverlay, position, geometricNormal, g_vWorldOverlayTiling[index]);
    factor = GetOverlayFactor(overlay.rgb, g_vWorldOverlayDarknessContrast[index], g_vWorldOverlayBrightnessContrast[index]);
    strength1 = g_vWorldOverlayStrengths1[index];
    strength2 = g_vWorldOverlayStrengths2[index];
#else
    vec4 overlay = SampleBiPlanar(g_tColorWorldOverlay, position, geometricNormal, g_flWorldOverlayTiling);
    factor = GetOverlayFactor(overlay.rgb, g_flWorldOverlayDarknessContrast, g_flWorldOverlayBrightnessContrast);
    strength1 = g_flWorldOverlayStrength1;
    strength2 = g_flWorldOverlayStrength2;
#endif
}

void ApplyWorldOverlayLayer3(inout Layer_t layer3, vec3 factor, int index)
{
#if (F_LAYER_COUNT == 1)
    #if (F_WORLD_OVERLAY == 2)
        float strength3 = g_vWorldOverlayStrengths3[index];
    #else
        float strength3 = g_flWorldOverlayStrength3;
    #endif
    layer3.Albedo = mix(layer3.Albedo, layer3.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask3, layer3.TintMask) * strength3);
#endif
}
#endif

void ApplyWorldOverlay(vec3 geometricNormal, inout Layer_t layer1, inout Layer_t layer2, inout Layer_t layer3)
{
#if (F_WORLD_OVERLAY > 0)
    vec3 position = GetWorldOverlayPosition();

    for (int i = 0; i < F_WORLD_OVERLAY; i++)
    {
        vec3 factor;
        float strength1;
        float strength2;
        SampleWorldOverlay(i, position, geometricNormal, factor, strength1, strength2);

        layer1.Albedo = mix(layer1.Albedo, layer1.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask1, layer1.TintMask) * strength1);
        layer2.Albedo = mix(layer2.Albedo, layer2.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask2, layer2.TintMask) * strength2);
        ApplyWorldOverlayLayer3(layer3, factor, i);
    }
#endif
}

void ApplyUvOverlay(vec2 texCoord, vec3 geometricNormal, inout Layer_t layer1, inout Layer_t layer2, inout Layer_t layer3)
{
#if (F_UV_OVERLAY == 1)
    vec2 overlayTexCoord = GetOverlayTexCoord(texCoord);

    // The overlay carries its own tangent frame unless it rides on the untransformed
    // primary UVs, in which case the mesh frame is what the layers already use.
    bool derived = g_bUseSecondaryUvForOverlay || any(notEqual(g_vOverlayScaleRotationTranslation, vec4(1.0, 0.0, 0.0, 0.0)));
    LayerMapping_t overlayMapping = GetDerivedMapping(overlayTexCoord, geometricNormal);
    vec3 overlayTangent = derived ? overlayMapping.Tangent : vTangentOut.xyz;
    vec3 overlayBitangent = derived ? overlayMapping.Bitangent : GetBitangent(vNormalOut, vTangentOut);

    vec4 overlayColor = texture(g_tColorOverlay, overlayTexCoord);
    vec4 overlayNormalRoughness = texture(g_tNormalRoughnessOverlay, overlayTexCoord);

    vec2 overlayGradient = GetSurfaceGradient(overlayNormalRoughness.rg);
    vec3 overlayNormalOffset = overlayTangent * overlayGradient.x + overlayBitangent * overlayGradient.y;
    float overlayRoughness = UnpackFromColor(overlayNormalRoughness.b);

    vec3 factor = GetOverlayFactor(overlayColor.rgb, g_flOverlayDarknessContrast, g_flOverlayBrightnessContrast);

    ApplyOverlayToLayer(layer1, factor, overlayNormalOffset, overlayRoughness,
        g_nOverlayTintMask1, g_flOverlayStrength1, g_flOverlayNormalStrength1, g_flOverlayRoughnessStrength1);
    ApplyOverlayToLayer(layer2, factor, overlayNormalOffset, overlayRoughness,
        g_nOverlayTintMask2, g_flOverlayStrength2, g_flOverlayNormalStrength2, g_flOverlayRoughnessStrength2);

    #if (F_LAYER_COUNT == 1)
        ApplyOverlayToLayer(layer3, factor, overlayNormalOffset, overlayRoughness,
            g_nOverlayTintMask3, g_flOverlayStrength3, g_flOverlayNormalStrength3, g_flOverlayRoughnessStrength3);
    #endif
#endif
}

float GetLayerWeight(float blend, float threshold, float softness)
{
    return (blend >= 1.0) ? 1.0 : smoothstep(-softness, softness, blend - threshold);
}

float GetLayerWeight3(float blend3, float threshold3)
{
#if (F_LAYER_COUNT == 1)
    return GetLayerWeight(blend3, threshold3, g_flRevealSoftness3);
#else
    return 0.0;
#endif
}

void CompositeLayer(inout Layer_t result, inout vec3 illumColor, Layer_t layer, vec3 layerIllumColor, float weight)
{
    result.Albedo = mix(result.Albedo, layer.Albedo, weight);
    result.NormalOffset = mix(result.NormalOffset, layer.NormalOffset, weight);
    result.Roughness = mix(result.Roughness, layer.Roughness, weight);
    result.Metalness = mix(result.Metalness, layer.Metalness, weight);
    result.SelfIllum = mix(result.SelfIllum, layer.SelfIllum, weight);
    illumColor = mix(illumColor, layerIllumColor, weight);
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    float blend2;
    float blend3;
    GetPaintBlends(blend2, blend3);
    blend2 = ApplyRevealOverlay(blend2, texCoord);

    vec3 illumColor1;
    vec3 illumColor2;
    vec3 illumColor3;
    Layer_t layer1 = SampleLayer1(texCoord, mat.GeometricNormal, illumColor1);
    Layer_t layer2 = SampleLayer2(texCoord, mat.GeometricNormal, illumColor2);

    float threshold2 = GetRevealThreshold2(texCoord);
    ApplyBorder2(layer1, layer2, blend2, threshold2);

    Layer_t layer3 = SampleLayer3(texCoord, mat.GeometricNormal, illumColor3);
    float threshold3 = GetRevealThreshold3(texCoord);
    ApplyBorder3(layer1, layer2, layer3, blend3, threshold3);

    ApplyWorldOverlay(mat.GeometricNormal, layer1, layer2, layer3);
    ApplyUvOverlay(texCoord, mat.GeometricNormal, layer1, layer2, layer3);

    Layer_t result = layer1;
    vec3 illumColor = illumColor1;
    float layer2Weight = GetLayerWeight(blend2, threshold2, g_flRevealSoftness2);
    CompositeLayer(result, illumColor, layer2, illumColor2, layer2Weight);
    CompositeLayer(result, illumColor, layer3, illumColor3, GetLayerWeight3(blend3, threshold3));

    ResolveLayerMaterial(mat, result, illumColor);

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    // Stored as it is, without the scale or a mask
    mat.BloomAmount = saturate(mix(g_flOverrideBloomAmount1, g_flOverrideBloomAmount2, layer2Weight));
#endif

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendValues = vec3(vColorBlendValues.xy, 0.0);
    RenderSurface(g_tColor1, options);
}
