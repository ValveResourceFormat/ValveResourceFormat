#version 460

#include "csgo_environment_blend_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_height_layers.slang"
#include "../common/surface_height_blend.slang"
#include "../common/wetness.slang"

in vec4 vTintColor_ModelAmount;
in vec4 vColorBlendValues;
in vec4 vTexCoord2; // Layer 2 in xy, its detail in zw

#if (F_SHARED_COLOR_OVERLAY == 1)
    in vec2 vOverlayTexCoord;
#endif

#if (F_ENABLE_LAYER_3 == 1)
    in vec4 vTexCoord3; // Layer 3 in xy, its detail in zw
#endif

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
#endif

// Height-band method instead of normalized two-height blend
uniform int F_USE_NEW_BLENDING;

// Material 1
#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference1 = 0.5;
    uniform float g_flAntiAliasedEdgeStrength1 = 1.0;
#endif
uniform sampler2D g_tColor1; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    uniform sampler2D g_tNormalDetail1;
    uniform int g_nDetailUVSet1 = -1; // -1=Inherit, 0=Biplanar, 1=UV1, 2=UV2
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);
    uniform float g_fDetailTextureNormalContrast1 = 1.0; // -2 .. 2
    uniform float g_flDetailTexCoordRotation1 = 0.0;
#endif

uniform bool g_bMetalness1 = true;
uniform bool g_bModelTint1 = true;
uniform int g_nColorCorrectionMode1 = 0; // 1 = adjust the base color through g_mTextureAdjust1
uniform float g_fTextureNormalContrast1 = 1.0;
uniform float g_fTextureRoughnessBrightness1 = 1.0;
uniform float g_fTextureRoughnessContrast1 = 1.0;
uniform float g_fTintMaskBrightness1 = 1.0;
uniform float g_fTintMaskContrast1 = 1.0;
uniform int g_nVertexColorMode1 = 0; // 0=Masked by tint, 1=Unmasked, 2=Disabled
uniform vec3 g_vAmbientOcclusionLevels1 = vec3(0, 0.5, 1);

uniform float g_flHeightMapScale1 = 1.0;
uniform float g_flHeightMapZeroPoint1 = 0.5;

uniform mat4 g_mTextureColorAdjust1;
uniform mat4 g_mTextureAdjust1;

uniform int g_nUVSet1 = 1; // 0=Biplanar, 1=UV1, 2=UV2
uniform vec2 g_vTexCoordScale1 = vec2(1.0);
uniform float g_flTexCoordRotation1 = 0.0;

// Material 2
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tHeight2;
uniform sampler2D g_tNormal2;

#if (F_DETAIL_NORMAL == 1)
    uniform sampler2D g_tNormalDetail2;
    uniform int g_nDetailUVSet2 = -1;
    uniform vec2 g_vDetailTexCoordScale2 = vec2(1.0);
    uniform float g_fDetailTextureNormalContrast2 = 1.0;
    uniform float g_flDetailTexCoordRotation2 = 0.0;
#endif

#if (F_SHARED_COLOR_OVERLAY == 1)
    uniform sampler2D g_tSharedColorOverlay; // SrgbRead(true)

    uniform int g_nColorOverlayUVSet = 2;
    uniform vec2 g_vOverlayTexCoordScale = vec2(1.0);

    uniform vec3 g_vColorOverlayLayerStrengths = vec3(1.0);
    uniform vec3 g_vColorOverlayTintMaskStrengths = vec3(0.0); // -1 .. 1
    uniform float g_flOverlayDarknessContrast = 1.0;    // 0.2 .. 2.0
    uniform float g_flOverlayBrightnessContrast = 1.0;  // 0.2 .. 2.0

    // The previous blending picks the layers and the tint masks of the overlay with these instead of the strengths
    uniform int g_nColorOverlayMode = 0;        // 0=All layers, 1..3=Only that layer
    uniform int g_nColorOverlayTintMask = 0;    // 0=Mask all, 1..3=Mask only that layer, 4=Unmasked
#endif

#if (F_BLEND_EFFECTS_2 == 1)
    uniform int F_BORDER_BLEND_MODE_2; // 0="Multiply", 1="Replace", 2="Mod2x", 3="Colorize"
    uniform int F_BORDER_ROUGHNESS_2;

    uniform float g_flBorderSpread2 = 0.1;
    uniform float g_flBorderSoftness2 = 0.1;
    uniform float g_flBorderOffset2 = 0.0;
    uniform vec3 g_vBorderTint2 = vec3(1.0); // SrgbRead(true)
    uniform vec2 g_vBorderLayerAmount2 = vec2(1.0, 0.0);
    uniform bool g_bBorderTintMask2 = false;
    uniform float g_fBorderRoughness2 = 0.5;

    uniform float g_flBevelSpread2 = 0.1;
    uniform float g_flBevelSoftness2 = 0.1;
    uniform float g_flBevelOffset2 = 0.0;
    uniform float g_flBevelStrength2 = 0.0; // -1 .. 1, in height units of the old bevel ramp
    uniform float g_flBevelCurve2 = 0.0;
    uniform vec2 g_vBevelLayerAmount2 = vec2(1.0);
#endif

uniform bool g_bMetalness2 = true;
uniform bool g_bModelTint2 = true;
uniform int g_nColorCorrectionMode2 = 0;
uniform float g_fTextureNormalContrast2 = 1.0;
uniform float g_fTextureRoughnessBrightness2 = 1.0;
uniform float g_fTextureRoughnessContrast2 = 1.0;
uniform float g_fTintMaskBrightness2 = 1.0;
uniform float g_fTintMaskContrast2 = 1.0;
uniform int g_nVertexColorMode2 = 0;
uniform vec3 g_vAmbientOcclusionLevels2 = vec3(0, 0.5, 1);

uniform int g_nUVSet2 = 1;
uniform vec2 g_vTexCoordScale2 = vec2(1.0);
uniform float g_flTexCoordRotation2 = 0.0;

uniform float g_flHeightMapScale2 = 1.0;
uniform float g_flHeightMapZeroPoint2 = 0.5;

uniform mat4 g_mTextureColorAdjust2;
uniform mat4 g_mTextureAdjust2;

#if (F_ENABLE_LAYER_3 == 1)
    uniform sampler2D g_tColor3; // SrgbRead(true)
    uniform sampler2D g_tHeight3;
    uniform sampler2D g_tNormal3;

    #if (F_DETAIL_NORMAL == 1)
        uniform sampler2D g_tNormalDetail3;
        uniform int g_nDetailUVSet3 = -1;
        uniform vec2 g_vDetailTexCoordScale3 = vec2(1.0);
        uniform float g_fDetailTextureNormalContrast3 = 1.0;
        uniform float g_flDetailTexCoordRotation3 = 0.0;
    #endif

    uniform bool g_bMetalness3 = true;
    uniform bool g_bModelTint3 = true;
    uniform int g_nColorCorrectionMode3 = 0;
    uniform float g_fTextureNormalContrast3 = 1.0;
    uniform float g_fTextureRoughnessBrightness3 = 1.0;
    uniform float g_fTextureRoughnessContrast3 = 1.0;
    uniform float g_fTintMaskBrightness3 = 1.0;
    uniform float g_fTintMaskContrast3 = 1.0;
    uniform int g_nVertexColorMode3 = 0;
    uniform vec3 g_vAmbientOcclusionLevels3 = vec3(0, 0.5, 1);

    uniform int g_nUVSet3 = 1;
    uniform vec2 g_vTexCoordScale3 = vec2(1.0);
    uniform float g_flTexCoordRotation3 = 0.0;

    uniform float g_flHeightMapScale3 = 1.0;
    uniform float g_flHeightMapZeroPoint3 = 0.5;

    uniform mat4 g_mTextureColorAdjust3;
    uniform mat4 g_mTextureAdjust3;

    #if (F_BLEND_EFFECTS_3 == 1)
        uniform int F_BORDER_BLEND_MODE_3;
        uniform int F_BORDER_ROUGHNESS_3;

        uniform float g_flBorderSpread3 = 0.1;
        uniform float g_flBorderSoftness3 = 0.1;
        uniform float g_flBorderOffset3 = 0.0;
        uniform vec3 g_vBorderTint3 = vec3(1.0); // SrgbRead(true)
        uniform vec2 g_vBorderLayerAmount3 = vec2(1.0, 0.0);
        uniform bool g_bBorderTintMask3 = false;
        uniform float g_fBorderRoughness3 = 0.5;

        uniform float g_flBevelSpread3 = 0.1;
        uniform float g_flBevelSoftness3 = 0.1;
        uniform float g_flBevelOffset3 = 0.0;
        uniform float g_flBevelStrength3 = 0.0; // -1 .. 1, in height units of the old bevel ramp
        uniform float g_flBevelCurve3 = 0.0;
        uniform vec2 g_vBevelLayerAmount3 = vec2(1.0);
    #endif
#endif

// Color composite parameters for both blend paths. At these defaults, a plain lerp to the upper layer.
uniform float g_flColorOverlay2 = 0.0;
uniform float g_flColorReplace2 = 1.0;
uniform float g_flNormalCombine2 = 0.0; // 0 .. 8
uniform float g_flNormalReplace2 = 1.0;
uniform float g_flRoughnessCombine2 = 0.0;
uniform float g_flRoughnessReplace2 = 1.0;
uniform float g_flAOCombine2 = 0.0;
uniform float g_flAOReplace2 = 1.0;
uniform float g_flMetalnessCombine2 = 0.0;
uniform float g_flMetalnessReplace2 = 1.0;
#if (F_ENABLE_LAYER_3 == 1)
    uniform float g_flColorOverlay3 = 0.0;
    uniform float g_flColorReplace3 = 1.0;
    uniform float g_flNormalCombine3 = 0.0;
    uniform float g_flNormalReplace3 = 1.0;
    uniform float g_flRoughnessCombine3 = 0.0;
    uniform float g_flRoughnessReplace3 = 1.0;
    uniform float g_flAOCombine3 = 0.0;
    uniform float g_flAOReplace3 = 1.0;
    uniform float g_flMetalnessCombine3 = 0.0;
    uniform float g_flMetalnessReplace3 = 1.0;
#endif

uniform float g_flBlendSoftnessDistanceModifierStrength = 1.0;

uniform float g_flBlendSoftness2 = 0.01;
uniform float g_flUnderlyingHeightMapInfluence2 = 1.0;
uniform float g_flMaskWithHeight2 = 0.0;

// Fades the paint of a layer by how much the surface faces a direction
uniform int F_BLEND_BY_FACING_DIRECTION_2;
uniform vec3 g_vFacingDirection2 = vec3(0.0, 0.0, 1.0);
uniform float g_flFacingDirectionMaskSpread2 = 0.5;
uniform float g_vFacingDirectionMaskNormalMapStrength2 = 1.0; // 0 .. 4
uniform float g_vFacingDirectionMaskSoftness2 = 0.1;

#if (F_ENABLE_LAYER_3 == 1)
    uniform float g_flBlendSoftness3 = 0.01;
    uniform float g_flUnderlyingHeightMapInfluence3 = 1.0;
    uniform float g_flMaskWithHeight3 = 0.0;

    uniform int F_BLEND_BY_FACING_DIRECTION_3;
    uniform vec3 g_vFacingDirection3 = vec3(0.0, 0.0, 1.0);
    uniform float g_flFacingDirectionMaskSpread3 = 0.5;
    uniform float g_vFacingDirectionMaskNormalMapStrength3 = 1.0;
    uniform float g_vFacingDirectionMaskSoftness3 = 0.1;
#endif

// The dielectric reflectance the specular color is built from. Wetness pulls it toward water.
uniform float g_flReflectance = 0.04;

// The layers composited so far
struct LayerStack_t
{
    vec3 Color;
    float Occlusion;
    float Roughness;
    vec3 NormalTs;
    vec4 Height;
    float WetHeight;
    float RawHeight;
};

// How an upper layer combines with the layers below it
struct LayerCombine_t
{
    float NormalCombine;
    float NormalReplace;
    float ColorOverlay;
    float ColorReplace;
    float AOCombine;
    float AOReplace;
    float RoughnessCombine;
    float RoughnessReplace;
    float MetalnessCombine;
    float MetalnessReplace;
};

float CombineMetalness(float lower, float upper, float weight, float combine, float replace)
{
    return mix(lower * mix(1.0, upper * 2.0, weight * combine), upper, weight * replace);
}

void CompositeLayerStack(inout LayerStack_t stack, float occlusion, vec3 color, float roughness, vec3 normalTs, vec4 height,
    float weight, float weightBase, LayerCombine_t combine)
{
    stack.NormalTs = CombineNormal(stack.NormalTs, normalTs, weight, combine.NormalCombine, combine.NormalReplace);

    stack.Color = CombineColor(stack.Color, color, weight, combine.ColorOverlay, combine.ColorReplace);
    stack.Occlusion = CombineOcclusion(stack.Occlusion, occlusion, weight, combine.AOCombine, combine.AOReplace);
    stack.Roughness = CombineRoughness(stack.Roughness, roughness, weight, combine.RoughnessCombine, combine.RoughnessReplace);

    float metalness = CombineMetalness(stack.Height.a, height.a, weight, combine.MetalnessCombine, combine.MetalnessReplace);
    stack.Height = stack.Height * weightBase + height * weight;
    stack.Height.a = metalness;
}

// What both upper layers need to weigh themselves against the layers below
struct BlendState_t
{
    bool NewBlending;
    float BlendFactor2;
    float Paint3;       // Layer 3's blend factor before its facing direction, which reads the normal layer 2 leaves
    vec3 PaintShares;
    float RangeSoftness1;
    float RangeSoftness2;
};

HeightLayer_t SampleLayer1(vec2 texCoord, BiplanarFrame_t biplanar)
{
    return SampleHeightLayer(g_tColor1, g_tHeight1, g_tNormal1, g_nUVSet1, texCoord, g_vTexCoordScale1, biplanar,
        g_fTintMaskContrast1, g_fTintMaskBrightness1, g_fTextureRoughnessContrast1, g_fTextureRoughnessBrightness1,
        g_bMetalness1, g_mTextureAdjust1, g_mTextureColorAdjust1, g_nColorCorrectionMode1,
        vTintColor_ModelAmount, g_bModelTint1);
}

HeightLayer_t SampleLayer2(BiplanarFrame_t biplanar)
{
    return SampleHeightLayer(g_tColor2, g_tHeight2, g_tNormal2, g_nUVSet2, vTexCoord2.xy, g_vTexCoordScale2, biplanar,
        g_fTintMaskContrast2, g_fTintMaskBrightness2, g_fTextureRoughnessContrast2, g_fTextureRoughnessBrightness2,
        g_bMetalness2, g_mTextureAdjust2, g_mTextureColorAdjust2, g_nColorCorrectionMode2,
        vTintColor_ModelAmount, g_bModelTint2);
}

// Without a third layer this is an empty layer that never shows
HeightLayer_t SampleLayer3(BiplanarFrame_t biplanar)
{
#if (F_ENABLE_LAYER_3 == 1)
    return SampleHeightLayer(g_tColor3, g_tHeight3, g_tNormal3, g_nUVSet3, vTexCoord3.xy, g_vTexCoordScale3, biplanar,
        g_fTintMaskContrast3, g_fTintMaskBrightness3, g_fTextureRoughnessContrast3, g_fTextureRoughnessBrightness3,
        g_bMetalness3, g_mTextureAdjust3, g_mTextureColorAdjust3, g_nColorCorrectionMode3,
        vTintColor_ModelAmount, g_bModelTint3);
#else
    return HeightLayer_t(vec3(0.0), 0.0, vec4(0.0), vec2(0.0), 0.0, 0.0);
#endif
}

// A detail texture that inherits its layer's UV set also tiles with the layer's scale, on top of its own
vec2 DetailInheritedScale(int detailUvSet, vec2 layerScale)
{
    return (detailUvSet == -1) ? layerScale : vec2(1.0);
}

vec3 GetLayerNormal1(HeightLayer_t layer, BiplanarFrame_t biplanar, MaterialProperties_t mat)
{
    vec3 normalTs = LayerNormal(layer.EncodedNormal, g_tNormal1, g_nUVSet1, g_vTexCoordScale1,
        g_fTextureNormalContrast1, g_flTexCoordRotation1, biplanar, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_DETAIL_NORMAL == 1)
    vec2 inheritedScale1 = DetailInheritedScale(g_nDetailUVSet1, g_vTexCoordScale1);

    normalTs = ApplyDetailNormal(normalTs, g_tNormalDetail1, DetailUvSet(g_nDetailUVSet1, g_nUVSet1),
        vDetailTexCoords * inheritedScale1, g_vDetailTexCoordScale1 * inheritedScale1, g_fDetailTextureNormalContrast1,
        g_flDetailTexCoordRotation1, biplanar,
        mat.GeometricNormal, mat.Tangent, mat.Bitangent);
#endif

    return normalTs;
}

vec3 GetLayerNormal2(HeightLayer_t layer, BiplanarFrame_t biplanar, MaterialProperties_t mat)
{
    vec3 normalTs = LayerNormal(layer.EncodedNormal, g_tNormal2, g_nUVSet2, g_vTexCoordScale2,
        g_fTextureNormalContrast2, g_flTexCoordRotation2, biplanar, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_DETAIL_NORMAL == 1)
    vec2 inheritedScale2 = DetailInheritedScale(g_nDetailUVSet2, g_vTexCoordScale2);

    normalTs = ApplyDetailNormal(normalTs, g_tNormalDetail2, DetailUvSet(g_nDetailUVSet2, g_nUVSet2),
        vTexCoord2.zw * inheritedScale2, g_vDetailTexCoordScale2 * inheritedScale2, g_fDetailTextureNormalContrast2,
        g_flDetailTexCoordRotation2, biplanar,
        mat.GeometricNormal, mat.Tangent, mat.Bitangent);
#endif

    return normalTs;
}

#if (F_ENABLE_LAYER_3 == 1)
vec3 GetLayerNormal3(HeightLayer_t layer, BiplanarFrame_t biplanar, MaterialProperties_t mat)
{
    vec3 normalTs = LayerNormal(layer.EncodedNormal, g_tNormal3, g_nUVSet3, g_vTexCoordScale3,
        g_fTextureNormalContrast3, g_flTexCoordRotation3, biplanar, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_DETAIL_NORMAL == 1)
    vec2 inheritedScale3 = DetailInheritedScale(g_nDetailUVSet3, g_vTexCoordScale3);

    normalTs = ApplyDetailNormal(normalTs, g_tNormalDetail3, DetailUvSet(g_nDetailUVSet3, g_nUVSet3),
        vTexCoord3.zw * inheritedScale3, g_vDetailTexCoordScale3 * inheritedScale3, g_fDetailTextureNormalContrast3,
        g_flDetailTexCoordRotation3, biplanar,
        mat.GeometricNormal, mat.Tangent, mat.Bitangent);
#endif

    return normalTs;
}
#endif

vec3 TangentToWorld(vec3 normalTs, MaterialProperties_t mat)
{
    return mat.Tangent * normalTs.x + mat.Bitangent * normalTs.y + mat.GeometricNormal * normalTs.z;
}

BlendState_t GetBlendState(vec2 texCoord, HeightLayer_t layer1, HeightLayer_t layer2, vec3 normalTs1, MaterialProperties_t mat)
{
    BlendState_t blend;
    blend.NewBlending = (F_USE_NEW_BLENDING == 1);
    blend.BlendFactor2 = saturate(vColorBlendValues.x * 1.1 - 0.05);
    blend.Paint3 = saturate(vColorBlendValues.y * 1.1 - 0.05);

    // Each layer's share of the paint, which weighs how much it widens the softness
    blend.PaintShares = normalize(vec3(saturate(1.0 - blend.BlendFactor2 - blend.Paint3), blend.BlendFactor2, blend.Paint3));

    if (F_BLEND_BY_FACING_DIRECTION_2 != 0)
    {
        blend.BlendFactor2 *= GetFacingDirectionMask(g_vFacingDirection2, mat.GeometricNormal, TangentToWorld(normalTs1, mat),
            g_vFacingDirectionMaskNormalMapStrength2, g_flFacingDirectionMaskSpread2, g_vFacingDirectionMaskSoftness2);
    }

    blend.RangeSoftness1 = BlendSoftnessAtRange(g_tHeight1, texCoord, layer1.Height.r, g_flHeightMapScale1, blend.PaintShares.x,
        g_flBlendSoftnessDistanceModifierStrength);
    blend.RangeSoftness2 = BlendSoftnessAtRange(g_tHeight2, vTexCoord2.xy, layer2.Height.r, g_flHeightMapScale2, blend.PaintShares.y,
        g_flBlendSoftnessDistanceModifierStrength);

    return blend;
}

LayerStack_t StartLayerStack(HeightLayer_t layer1, vec3 normalTs)
{
    LayerStack_t stack;
    stack.Color = layer1.Albedo;
    stack.Roughness = layer1.Roughness;
    stack.NormalTs = normalTs;
    stack.Height = layer1.Height;

    // An alpha tested material keeps its occlusion in the height texture, and the color alpha is the opacity
#if (F_ALPHA_TEST == 1)
    stack.Occlusion = LayerOcclusion(layer1.Height.b, g_vAmbientOcclusionLevels1);
#else
    stack.Occlusion = LayerOcclusion(layer1.Occlusion, g_vAmbientOcclusionLevels1);
#endif

    // Wetness height and darkening blend up the layers the way colour does. Without the new blending, wetness
    // judges the layers' own heights instead.
    stack.WetHeight = WetnessHeight(layer1.Height.r, g_flHeightMapZeroPoint1, g_flHeightMapScale1);
    stack.RawHeight = WetnessHeightOld(layer1.Height.r, g_flHeightMapZeroPoint1, g_flHeightMapScale1);
    return stack;
}

// Both blend paths composite the same way and differ only in how they weigh the layer.
void BlendLayer2(inout LayerStack_t stack, BlendState_t blend, HeightLayer_t layer1, HeightLayer_t layer2,
    BiplanarFrame_t biplanar, MaterialProperties_t mat, out LayerCarry carry2, out OldBlend_t oldBlend, out float weight2,
    out float tintMaskBelow)
{
    float baseHeight1 = layer1.Height.r - g_flHeightMapZeroPoint1;
    float baseHeight2 = layer2.Height.r - g_flHeightMapZeroPoint2;

    float paintSoftness2 = max(min(vColorBlendValues.w + g_flBlendSoftness2, 1.0), 1e-4);

    // Layer 1's term lands twice here
    float softness2 = paintSoftness2 + blend.RangeSoftness1 * 2.0 + blend.RangeSoftness2;

    carry2 = LayerCarry(0.0, 0.0, 0.0, 0.0, 0.0);
    float compositeWeight2;
    float weightBase2;
    float normalReplaceGate2 = 1.0;

    if (blend.NewBlending)
    {
        LayerCarry carry1 = LayerCarry(0.0, 0.0, 0.0, 0.0, baseHeight1 * g_flHeightMapScale1);
        carry2 = BlendLayer(carry1, blend.BlendFactor2, layer2.Height.r,
            g_flHeightMapZeroPoint2, g_flHeightMapScale2, softness2,
            g_flUnderlyingHeightMapInfluence2, g_flMaskWithHeight2);

        weight2 = carry2.weight;
        compositeWeight2 = weight2;
        weightBase2 = 1.0 - weight2;
    }
    else
    {
        float softnessBelow2 = paintSoftness2 + blend.RangeSoftness1;
        vec2 zeroShift2 = vec2(blend.RangeSoftness1, blend.RangeSoftness1 + blend.RangeSoftness2) * 0.5;

        oldBlend = GetOldBlend(
            vec2(layer1.Height.r, layer2.Height.r),
            vec2(g_flHeightMapScale1, g_flHeightMapScale2),
            vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
            blend.BlendFactor2, softnessBelow2, softness2, zeroShift2, g_flMaskWithHeight2);

        weight2 = oldBlend.Weight;
        normalReplaceGate2 = GetOldNormalReplaceGate(oldBlend);

        // Layer 2 stays out of the composite until it is more than a trace
        compositeWeight2 = (weight2 > 0.05) ? weight2 : 0.0;
        weightBase2 = 1.0 - compositeWeight2;
    }

    // Tint mask of everything composited so far, used for masking borders
    tintMaskBelow = mix(layer1.TintMask, layer2.TintMask, weight2);

    vec3 color2 = layer2.Albedo;
    float roughness2 = layer2.Roughness;

#if (F_BLEND_EFFECTS_2 == 1)
    SeamFrame seam2 = GetSeamFrame(carry2.difference, blend.BlendFactor2,
        max(abs(g_flHeightMapScale1), abs(g_flHeightMapScale2)), softness2,
        weight2, layer2.Height.r, g_flMaskWithHeight2, tintMaskBelow);

    BorderParams border2 = BorderParams(g_flBorderSpread2, g_flBorderSoftness2, g_flBorderOffset2, g_vBorderTint2,
        g_vBorderLayerAmount2, g_bBorderTintMask2, F_BORDER_BLEND_MODE_2,
        F_BORDER_ROUGHNESS_2 == 1, g_fBorderRoughness2);

    if (blend.NewBlending)
    {
        ApplyBlendBorder(stack.Color, color2, stack.Roughness, roughness2, seam2, border2);
    }
    else
    {
        ApplyOldBlendBorder(stack.Color, color2, stack.Roughness, roughness2, oldBlend, blend.BlendFactor2,
            tintMaskBelow, border2);
    }
#endif

    stack.WetHeight = mix(stack.WetHeight, WetnessHeight(layer2.Height.r, g_flHeightMapZeroPoint2, g_flHeightMapScale2), compositeWeight2);
    float wetHeightOld2 = WetnessHeightOld(layer2.Height.r, g_flHeightMapZeroPoint2, g_flHeightMapScale2);
    stack.RawHeight = (stack.RawHeight * weightBase2) + (wetHeightOld2 * compositeWeight2);

    stack.Height.r = baseHeight1;
    vec4 height2 = layer2.Height;
    height2.r = baseHeight2;

    vec3 normalTs2 = GetLayerNormal2(layer2, biplanar, mat);

#if (F_BLEND_EFFECTS_2 == 1)
    BevelParams bevel2 = BevelParams(g_flBevelSpread2, g_flBevelSoftness2, g_flBevelOffset2,
        g_flBevelStrength2, g_flBevelCurve2, g_vBevelLayerAmount2);

    if (blend.NewBlending)
    {
        stack.NormalTs = BlendBevelNormal(stack.NormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent, vFragPosition, seam2, bevel2);
    }
    else
    {
        stack.NormalTs = BlendBevelNormalOld(stack.NormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent, vFragPosition,
            oldBlend, blend.BlendFactor2, bevel2);
    }
#endif

    LayerCombine_t combine2 = LayerCombine_t(g_flNormalCombine2, g_flNormalReplace2 * normalReplaceGate2, g_flColorOverlay2,
        g_flColorReplace2, g_flAOCombine2, g_flAOReplace2, g_flRoughnessCombine2, g_flRoughnessReplace2,
        g_flMetalnessCombine2, g_flMetalnessReplace2);

    float occlusion2 = LayerOcclusion(layer2.Occlusion, g_vAmbientOcclusionLevels2);

    CompositeLayerStack(stack, occlusion2, color2, roughness2, normalTs2, height2, compositeWeight2, weightBase2, combine2);
}

// Does nothing without a third layer, and leaves weight3 at 0
void BlendLayer3(inout LayerStack_t stack, BlendState_t blend, HeightLayer_t layer3, BiplanarFrame_t biplanar,
    MaterialProperties_t mat, LayerCarry carry2, OldBlend_t oldBlend2, float weight2, float tintMaskBelow, out float weight3)
{
    weight3 = 0.0;

#if (F_ENABLE_LAYER_3 == 1)
    float baseHeight3 = layer3.Height.r - g_flHeightMapZeroPoint3;

    // The facing direction reads the normal layers 1 and 2 leave
    float blendFactor3 = blend.Paint3;

    if (F_BLEND_BY_FACING_DIRECTION_3 != 0)
    {
        blendFactor3 *= GetFacingDirectionMask(g_vFacingDirection3, mat.GeometricNormal, TangentToWorld(stack.NormalTs, mat),
            g_vFacingDirectionMaskNormalMapStrength3, g_flFacingDirectionMaskSpread3, g_vFacingDirectionMaskSoftness3);
    }

    float paintSoftness3 = max(min(vColorBlendValues.w + g_flBlendSoftness3, 1.0), 1e-4);
    float rangeSoftness3 = BlendSoftnessAtRange(g_tHeight3, vTexCoord3.xy, layer3.Height.r, g_flHeightMapScale3, blend.PaintShares.z,
        g_flBlendSoftnessDistanceModifierStrength);
    float softness3 = paintSoftness3 + blend.RangeSoftness1 + blend.RangeSoftness2 + rangeSoftness3;

    LayerCarry carry3 = LayerCarry(0.0, 0.0, 0.0, 0.0, 0.0);
    OldBlend_t oldBlend3;
    float compositeWeight3;
    float normalReplaceGate3 = 1.0;

    if (blend.NewBlending)
    {
        carry3 = BlendLayer(carry2, blendFactor3, layer3.Height.r,
            g_flHeightMapZeroPoint3, g_flHeightMapScale3, softness3,
            g_flUnderlyingHeightMapInfluence3, g_flMaskWithHeight3);

        weight3 = carry3.weight;
        compositeWeight3 = weight3;
    }
    else
    {
        float zeroShift3 = (blend.RangeSoftness1 + blend.RangeSoftness2 + rangeSoftness3) * 0.5;

        oldBlend3 = GetOldBlend3(oldBlend2, layer3.Height.r, g_flHeightMapScale3, g_flHeightMapZeroPoint3,
            blendFactor3, softness3, zeroShift3, g_flMaskWithHeight3);

        weight3 = oldBlend3.Weight;
        normalReplaceGate3 = GetOldNormalReplaceGate(oldBlend3);

        // Layer 3 stays out of the composite until it is more than a trace
        compositeWeight3 = (weight3 > 0.05) ? weight3 : 0.0;
    }

    vec3 color3 = layer3.Albedo;
    float roughness3 = layer3.Roughness;

    #if (F_BLEND_EFFECTS_3 == 1)
        BorderParams border3 = BorderParams(g_flBorderSpread3, g_flBorderSoftness3, g_flBorderOffset3, g_vBorderTint3,
            g_vBorderLayerAmount3, g_bBorderTintMask3, F_BORDER_BLEND_MODE_3,
            F_BORDER_ROUGHNESS_3 == 1, g_fBorderRoughness3);

        // Layer 3 decorates its seam against the composite below it, so both its height
        // scale and its tint mask come from layers 1 and 2 blended together.
        float tintMask3 = mix(tintMaskBelow, layer3.TintMask, weight3);

        SeamFrame seam3 = GetSeamFrame(carry3.difference, blendFactor3,
            max(abs(mix(g_flHeightMapScale1, g_flHeightMapScale2, weight2)), abs(g_flHeightMapScale3)),
            softness3, weight3, layer3.Height.r, g_flMaskWithHeight3, tintMask3);

        if (blend.NewBlending)
        {
            ApplyBlendBorder(stack.Color, color3, stack.Roughness, roughness3, seam3, border3);
        }
        else
        {
            ApplyOldBlendBorder(stack.Color, color3, stack.Roughness, roughness3, oldBlend3, blendFactor3, tintMask3, border3);
        }
    #endif

    float weightBase3 = 1.0 - compositeWeight3;

    stack.WetHeight = mix(stack.WetHeight, WetnessHeight(layer3.Height.r, g_flHeightMapZeroPoint3, g_flHeightMapScale3), compositeWeight3);

    float wetHeightOld3 = WetnessHeightOld(layer3.Height.r, g_flHeightMapZeroPoint3, g_flHeightMapScale3);
    stack.RawHeight = (stack.RawHeight * weightBase3) + (wetHeightOld3 * compositeWeight3);

    vec4 height3 = layer3.Height;
    height3.r = baseHeight3;

    vec3 normalTs3 = GetLayerNormal3(layer3, biplanar, mat);

    #if (F_BLEND_EFFECTS_3 == 1)
        BevelParams bevel3 = BevelParams(g_flBevelSpread3, g_flBevelSoftness3, g_flBevelOffset3,
            g_flBevelStrength3, g_flBevelCurve3, g_vBevelLayerAmount3);

        if (blend.NewBlending)
        {
            stack.NormalTs = BlendBevelNormal(stack.NormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent, vFragPosition,
                seam3, bevel3);
        }
        else
        {
            stack.NormalTs = BlendBevelNormalOld(stack.NormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent, vFragPosition,
                oldBlend3, blendFactor3, bevel3);
        }
    #endif

    LayerCombine_t combine3 = LayerCombine_t(g_flNormalCombine3, g_flNormalReplace3 * normalReplaceGate3, g_flColorOverlay3,
        g_flColorReplace3, g_flAOCombine3, g_flAOReplace3, g_flRoughnessCombine3, g_flRoughnessReplace3,
        g_flMetalnessCombine3, g_flMetalnessReplace3);

    float occlusion3 = LayerOcclusion(layer3.Occlusion, g_vAmbientOcclusionLevels3);

    CompositeLayerStack(stack, occlusion3, color3, roughness3, normalTs3, height3, compositeWeight3, weightBase3, combine3);
#endif
}

float GetWetDarkening(float share2, float weight3)
{
    float wetDarkening = mix(g_flWetnessDarkeningStrength1, g_flWetnessDarkeningStrength2, share2 * g_flColorReplace2);

#if (F_ENABLE_LAYER_3 == 1)
    wetDarkening = mix(wetDarkening, g_flWetnessDarkeningStrength3, weight3 * g_flColorReplace3);
#endif

    return wetDarkening;
}

#if (F_SHARED_COLOR_OVERLAY == 1)
// How much of the overlay a layer takes with the previous blending: the layers it applies to, and the ones whose tint mask gates it
float OldOverlayLayerAmount(float tintMask, int layer, float share)
{
    float layerEnabled = float(g_nColorOverlayMode == 0 || g_nColorOverlayMode == layer);
    float mask = (g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == layer) ? tintMask : 1.0;

    return mask * layerEnabled * share;
}
#endif

vec3 ApplySharedColorOverlay(vec3 color, bool newBlending, BiplanarFrame_t biplanar, HeightLayer_t layer1, HeightLayer_t layer2,
    HeightLayer_t layer3, float share1, float share2, float weight3)
{
#if (F_SHARED_COLOR_OVERLAY == 1)
    float overlayAmount;

    if (newBlending)
    {
        overlayAmount =
            OverlayLayerAmount(layer1.TintMask, g_vColorOverlayTintMaskStrengths.x, g_vColorOverlayLayerStrengths.x, share1)
            + OverlayLayerAmount(layer2.TintMask, g_vColorOverlayTintMaskStrengths.y, g_vColorOverlayLayerStrengths.y, share2);

        #if (F_ENABLE_LAYER_3 == 1)
            overlayAmount += OverlayLayerAmount(layer3.TintMask, g_vColorOverlayTintMaskStrengths.z, g_vColorOverlayLayerStrengths.z, weight3);
        #endif
    }
    else
    {
        overlayAmount =
            OldOverlayLayerAmount(layer1.TintMask, 1, share1)
            + OldOverlayLayerAmount(layer2.TintMask, 2, share2);

        #if (F_ENABLE_LAYER_3 == 1)
            overlayAmount += OldOverlayLayerAmount(layer3.TintMask, 3, weight3);
        #endif
    }

    if (overlayAmount > 0.0)
    {
        vec3 overlay = UnpackFromColor(SampleMaterialTexture(g_tSharedColorOverlay, g_nColorOverlayUVSet,
            vOverlayTexCoord, g_vOverlayTexCoordScale, biplanar).rgb);
        vec3 overlayFactor = max(vec3(0.0), vec3(1.0)
            + (vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast
            + (SafePow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast);

        color *= mix(vec3(1.0), overlayFactor, overlayAmount);
    }
#endif

    return color;
}

vec3 ApplyVertexColor(vec3 color, HeightLayer_t layer1, HeightLayer_t layer2, HeightLayer_t layer3,
    float share1, float share2, float weight3)
{
    float vertexColorMasked = layer1.TintMask * share1 + layer2.TintMask * share2;
    float vertexColorUnmasked = float(g_nVertexColorMode1 != 0) * share1 + float(g_nVertexColorMode2 != 0) * share2;
    float vertexColorEnabled = float(g_nVertexColorMode1 != 2) * share1 + float(g_nVertexColorMode2 != 2) * share2;

#if (F_ENABLE_LAYER_3 == 1)
    vertexColorMasked += layer3.TintMask * weight3;
    vertexColorUnmasked += float(g_nVertexColorMode3 != 0) * weight3;
#endif

    float vertexColorMask = saturate(vertexColorUnmasked + vertexColorMasked) * vertexColorEnabled;

#if (F_ENABLE_LAYER_3 == 1)
    vertexColorMask += float(g_nVertexColorMode3 != 2) * weight3;
#endif

    return color * mix(vec3(1.0), vVertexColorOut.rgb, saturate(vertexColorMask));
}

void ApplyAlphaTest(inout MaterialProperties_t mat, HeightLayer_t layer1, vec2 texCoord)
{
#if (F_ALPHA_TEST == 1)
    mat.Opacity = AlphaTestAntiAliasing(layer1.Occlusion, texCoord, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength1);
#endif
}

// Get material properties from two or three layers
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    BiplanarFrame_t biplanar = GetBiplanarFrame(mat.GeometricNormal, vFragPosition);

    HeightLayer_t layer1 = SampleLayer1(texCoord, biplanar);
    HeightLayer_t layer2 = SampleLayer2(biplanar);
    HeightLayer_t layer3 = SampleLayer3(biplanar);

    vec3 normalTs1 = GetLayerNormal1(layer1, biplanar, mat);
    BlendState_t blend = GetBlendState(texCoord, layer1, layer2, normalTs1, mat);
    LayerStack_t stack = StartLayerStack(layer1, normalTs1);

    LayerCarry carry2;
    OldBlend_t oldBlend2;
    float weight2;
    float tintMaskBelow;
    BlendLayer2(stack, blend, layer1, layer2, biplanar, mat, carry2, oldBlend2, weight2, tintMaskBelow);

    float weight3;
    BlendLayer3(stack, blend, layer3, biplanar, mat, carry2, oldBlend2, weight2, tintMaskBelow, weight3);

    float share2 = weight2 * (1.0 - weight3);
    float share1 = saturate((1.0 - share2) - weight3);

    stack.Color = ApplySharedColorOverlay(stack.Color, blend.NewBlending, biplanar, layer1, layer2, layer3, share1, share2, weight3);
    stack.Color = ApplyVertexColor(stack.Color, layer1, layer2, layer3, share1, share2, weight3);

    ApplyAlphaTest(mat, layer1, texCoord);

    InitHeightMaterial(mat, stack.Color, stack.Occlusion, stack.NormalTs, stack.Roughness, stack.Height.r, stack.Height.a);

    float reflectance = g_flReflectance;

    if (blend.NewBlending)
    {
        ApplyWetness(mat, stack.WetHeight, vColorBlendValues.z, GetWetDarkening(share2, weight3), reflectance);
    }
    else
    {
        ApplyWetnessOld(mat, stack.RawHeight, vColorBlendValues.z, vColorBlendValues.w, GetWetDarkening(share2, weight3), reflectance);
    }

    FinishHeightMaterial(mat, reflectance);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendValues = vColorBlendValues.rgb;
    RenderSurface(g_tColor1, options);
}
