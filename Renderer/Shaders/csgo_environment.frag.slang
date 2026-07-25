#version 460

#define GameVfx_csgo_environment_blend 0

#include "common/utils.slang"
#include "common/features.slang"
#include "csgo_environment_features.slang"

#define IBL 1

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord;
in vec4 vTexCoord2;
in vec4 vTintColor_ModelAmount;
centroid in vec4 vVertexColor_Alpha;
in vec4 vBlendColorTint;


out vec4 outputColor;

// Material 1
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
    uniform sampler2D g_tNormalDetail1;
#endif

uniform bool g_bMetalness1 = true;
uniform bool g_bModelTint1 = true;
uniform bool g_bAdjustBaseColor1 = false;
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

// Material 2
#if (GameVfx_csgo_environment_blend != 0)
    in vec4 vColorBlendValues;

    uniform sampler2D g_tColor2; // SrgbRead(true)
    uniform sampler2D g_tHeight2;
    uniform sampler2D g_tNormal2;

    #if (F_DETAIL_NORMAL == 1)
        uniform sampler2D g_tNormalDetail2;
    #endif

    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform sampler2D g_tSharedColorOverlay; // SrgbRead(true)

        uniform int g_nColorOverlayMode;                    // "0=Both Layers,1=Layer 1,2=Layer 2"
        uniform int g_nColorOverlayTintMask;                // "0=Mask Both Layers,1=Mask Layer 1,2=Mask Layer 2,3==Unmasked"
        uniform float g_flOverlayDarknessContrast = 1.0;    // 0.2 .. 2.0
        uniform float g_flOverlayBrightnessContrast = 1.0;  // 0.2 .. 2.0
    #endif

    #if (F_BLEND_EFFECTS > 0)
        // ...
    #endif

    uniform bool g_bMetalness2 = true;
    uniform bool g_bModelTint2 = true;
    uniform bool g_bAdjustBaseColor2 = false;
    uniform float g_fTextureNormalContrast2 = 1.0;
    uniform float g_fTextureRoughnessBrightness2 = 1.0;
    uniform float g_fTextureRoughnessContrast2 = 1.0;
    uniform float g_fTintMaskBrightness2 = 1.0;
    uniform float g_fTintMaskContrast2 = 1.0;
    uniform int g_nVertexColorMode2 = 0;
    uniform vec3 g_vAmbientOcclusionLevels2 = vec3(0, 0.5, 1);

    uniform float g_flHeightMapScale2 = 1.0;
    uniform float g_flHeightMapZeroPoint2 = 0.5;

    uniform mat4 g_mTextureColorAdjust2;
    uniform mat4 g_mTextureAdjust2;

    #if (F_ENABLE_LAYER_3 == 1)
        in vec4 vTexCoord3;

        uniform sampler2D g_tColor3; // SrgbRead(true)
        uniform sampler2D g_tHeight3;
        uniform sampler2D g_tNormal3;

        uniform bool g_bMetalness3 = true;
        uniform bool g_bModelTint3 = true;
        uniform bool g_bAdjustBaseColor3 = false;
        uniform float g_fTextureNormalContrast3 = 1.0;
        uniform float g_fTextureRoughnessBrightness3 = 1.0;
        uniform float g_fTextureRoughnessContrast3 = 1.0;
        uniform float g_fTintMaskBrightness3 = 1.0;
        uniform float g_fTintMaskContrast3 = 1.0;
        uniform int g_nVertexColorMode3 = 0;
        uniform vec3 g_vAmbientOcclusionLevels3 = vec3(0, 0.5, 1);

        uniform float g_flHeightMapScale3 = 1.0;
        uniform float g_flHeightMapZeroPoint3 = 0.5;

        uniform mat4 g_mTextureColorAdjust3;
        uniform mat4 g_mTextureAdjust3;
    #endif

    // Colour composite parameters (used by both old and new blending; the engine composites
    // both paths the same way. Defaults reproduce a plain lerp).
    uniform float g_flColorOverlay2 = 0.0;
    uniform float g_flColorReplace2 = 1.0;
    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flColorOverlay3 = 0.0;
        uniform float g_flColorReplace3 = 1.0;
    #endif

    // Colour composite matching the compiled shader (used by both old and new blending):
    // mod2x-overlay the gamma-encoded layer, then Replace toward it. Degenerates to
    // mix(a, b, weight) when overlay == 0 and replace == 1 (the parameter defaults).
    vec3 CombineColor(vec3 a, vec3 b, float weight, float overlay, float replace)
    {
        vec3 mod2x = SrgbLinearToGamma(saturate(b)) * 2.0;
        return mix(a * mix(vec3(1.0), mod2x, weight * overlay), b, weight * replace);
    }

    #if (F_USE_NEW_BLENDING == 1)
        // New height-band blend (S_USE_NEW_BLENDING). Each additional layer gets a single
        // [0,1] weight from a height "difference" that carries forward from the layer below,
        // then composites per-property with Overlay/Combine + Replace. Uniform defaults match
        // the shader's parameter table (dumped from the compiled .vcs).
        uniform float g_flBlendSoftness2 = 0.01;
        uniform float g_flUnderlyingHeightMapInfluence2 = 1.0;
        uniform float g_flMaskWithHeight2 = 0.0;
        uniform bool g_bBlendByFacingDirection2 = false;
        uniform vec3 g_vFacingDirection2 = vec3(0.0, 0.0, 1.0);
        uniform float g_flFacingDirectionMaskSpread2 = 0.5;
        uniform float g_vFacingDirectionMaskSoftness2 = 0.1;

        #if (F_ENABLE_LAYER_3 == 1)
            uniform float g_flBlendSoftness3 = 0.01;
            uniform float g_flUnderlyingHeightMapInfluence3 = 1.0;
            uniform float g_flMaskWithHeight3 = 0.0;
            uniform bool g_bBlendByFacingDirection3 = false;
            uniform vec3 g_vFacingDirection3 = vec3(0.0, 0.0, 1.0);
            uniform float g_flFacingDirectionMaskSpread3 = 0.5;
            uniform float g_vFacingDirectionMaskSoftness3 = 0.1;
        #endif

        struct LayerCarry
        {
            float weight;       // final [0,1] weight for this layer
            float signedRaw;    // raw 2*bf-1 (added to next layer's difference)
            float signedCarry;  // weight-scaled signed factor (subtracted from next layer)
            float underHeight;  // height the next layer's difference is measured against
        };

        float NewBlendWeightCore(float difference, float bf, float rawHeight, float vertexSoftness,
                                 float blendSoftness, float maskWithHeight)
        {
            float softness = max(min(vertexSoftness + blendSoftness, 1.0), 1e-4);
            float quarter = softness * 0.25;
            float edgeDenom = 0.02 + quarter;

            float crossfade = saturate(0.5 + difference / (2.0 * softness));
            float edgeHi = saturate((saturate(bf + difference * 0.1 + 0.2) - (0.98 - quarter)) / edgeDenom);
            crossfade = mix(crossfade, 1.0, edgeHi);
            float edgeLo = saturate((edgeDenom - saturate(bf + difference * 0.05 - 0.05)) / edgeDenom);
            crossfade = mix(crossfade, 0.0, edgeLo);

            float heightMask = mix(0.5, rawHeight, maskWithHeight);
            return saturate(crossfade * heightMask * 2.0);
        }

        LayerCarry GetLayer2WeightAndCarry(float bf2, float rawHeight2, float heightScaled1,
                                           float vertexSoftness, vec3 geometricNormal)
        {
            LayerCarry o;
            if (bf2 <= 0.0)
            {
                o.weight = 0.0;
                o.signedRaw = 0.0;
                o.signedCarry = 0.0;
                o.underHeight = heightScaled1;
                return o;
            }

            float heightScaled2 = (rawHeight2 - g_flHeightMapZeroPoint2) * g_flHeightMapScale2;

            float faceBf2 = bf2;
            if (g_bBlendByFacingDirection2)
            {
                faceBf2 = bf2 * saturate(((dot(g_vFacingDirection2, geometricNormal) - 1.0)
                    + g_flFacingDirectionMaskSpread2 * 2.0) / (g_vFacingDirectionMaskSoftness2 * 2.0) + 0.5);
            }

            float signedFactor2 = 2.0 * faceBf2 - 1.0;
            float heightPlusSigned2 = heightScaled2 + signedFactor2;
            float difference2 = heightPlusSigned2 - mix(0.0, heightScaled1, g_flUnderlyingHeightMapInfluence2);

            float weight2 = NewBlendWeightCore(difference2, faceBf2, rawHeight2, vertexSoftness,
                g_flBlendSoftness2, g_flMaskWithHeight2);

            o.weight = weight2;
            o.signedRaw = signedFactor2;
            o.signedCarry = mix(0.0, signedFactor2, weight2);
            o.underHeight = mix(heightScaled1, max(heightScaled1, heightPlusSigned2), weight2);
            return o;
        }

        #if (F_ENABLE_LAYER_3 == 1)
        float GetLayer3Weight(float bf3, float rawHeight3, LayerCarry carry2,
                              float vertexSoftness, vec3 geometricNormal)
        {
            float heightScaled3 = (rawHeight3 - g_flHeightMapZeroPoint3) * g_flHeightMapScale3;

            float faceBf3 = bf3;
            if (g_bBlendByFacingDirection3)
            {
                faceBf3 = bf3 * saturate(((dot(g_vFacingDirection3, geometricNormal) - 1.0)
                    + g_flFacingDirectionMaskSpread3 * 2.0) / (g_vFacingDirectionMaskSoftness3 * 2.0) + 0.5);
            }

            float difference3 = (heightScaled3 + ((2.0 * faceBf3 - 1.0) + max(carry2.signedRaw, 0.0)))
                - mix(carry2.signedCarry, carry2.underHeight, g_flUnderlyingHeightMapInfluence3);

            return NewBlendWeightCore(difference3, faceBf3, rawHeight3, vertexSoftness,
                g_flBlendSoftness3, g_flMaskWithHeight3);
        }
        #endif
    #endif

    vec2 GetBlendWeights(vec2 heightTex, vec2 heightScale, vec2 heightZero, vec4 vColorBlendValues)
    {
        float blendFactor = vColorBlendValues.x;
        float blendSoftness = vColorBlendValues.w;

        // Weight calculations
        float h1 = heightScale.x + blendSoftness;
        float height1 = heightTex.x * h1;

        float h2 = heightScale.y + blendSoftness;
        float h22 = heightTex.y * (heightScale.y - blendSoftness);

        float blend1 = (-heightZero.x * h1 - ((1.0 - heightZero.y) * h2)) - blendSoftness;
        float blend2 = (1.0 - heightZero.x) * h1 - ((-heightZero.y) * h2);

        float h2x = h22 + mix(blend1, blend2, blendFactor);

        vec2 weights = vec2(height1, h2x) - vec2(max(height1, h2x) - blendSoftness);

        // Clamp negative values
        weights = max(weights, vec2(0.0));

        // Bias towards material 1
        weights.x += 0.001;

        // Normalize
        weights = weights / vec2(weights.x + weights.y);

        return weights;
    }
#endif

uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"

#include "common/fog.slang"

// Must be last
#include "common/lighting.slang"

// Get material properties (possibly from two layers)
MaterialProperties_t GetMaterial(vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut, vBitangentOut);

    // vTexCoord = Layer1 (xy) shared color overlay (zw)
    // vTexCoord2 = Layer2 (xy) UV2 (zw)
    // vDetailTexCoords = Detail (normal) UVs

    vec4 color = texture(g_tColor1, vTexCoord.xy);
    vec4 height = texture(g_tHeight1, vTexCoord.xy);
    vec4 normal = texture(g_tNormal1, vTexCoord.xy);

    vec3 aoLevels = g_vAmbientOcclusionLevels1.xyz;

    float tintMask1 = saturate(((height.g - 0.5) * g_fTintMaskContrast1 + 0.5) * g_fTintMaskBrightness1);
    height.a = g_bMetalness1 ? height.a : 0.0;
    normal.rg = (normal.rg - 0.5) * g_fTextureNormalContrast1 + 0.5;
    normal.b = saturate(((normal.b - 0.5) * g_fTextureRoughnessContrast1 + 0.5) * g_fTextureRoughnessBrightness1);

    vec3 overlayFactor = vec3(1.0);

    #if (GameVfx_csgo_environment_blend != 0 && F_SHARED_COLOR_OVERLAY == 1)
        vec3 overlay = texture(g_tSharedColorOverlay, vTexCoord.zw).rgb * 2.0 - 1.0;
        vec3 _15235 = (((vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast)
            + ((SafePow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast)) + vec3(1.0);
        overlayFactor = max(vec3(0.0), _15235);
    #endif

    vec3 tintColorNorm = normalize(max(vTintColor_ModelAmount.xyz, vec3(0.001)));
    float tintColorNormLuma = GetLuma(tintColorNorm);

    vec4 colorMaybeColorCorrected = mix(color, vec4((color * g_mTextureAdjust1).rgb, color.a), bvec4(g_bAdjustBaseColor1));
    vec4 colorColorCorrectedTinted = color * g_mTextureColorAdjust1;

    vec3 colorTintedMaybeColorCorrected = mix(colorMaybeColorCorrected.rgb, colorColorCorrectedTinted.rgb, tintMask1);

    float tintAdjustedLuma = GetLuma(colorTintedMaybeColorCorrected);

    float tintColorAdjustedLumaRatio = tintAdjustedLuma / tintColorNormLuma;
    float tintAdjustedLuma3x = 3.0 * tintAdjustedLuma;

    float tintColorHighestPoint = max(vTintColor_ModelAmount.x, max(vTintColor_ModelAmount.y, vTintColor_ModelAmount.z));
    float highPointXLuma = tintAdjustedLuma3x * tintColorHighestPoint;
    vec3 tintResult = saturate(mix(colorTintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjustedLumaRatio, highPointXLuma), vec3((vTintColor_ModelAmount.w * tintMask1) * float(g_bModelTint1))));

    color.rgb = tintResult;

    #if (GameVfx_csgo_environment_blend != 0)
        #if (F_SHARED_COLOR_OVERLAY == 1)
            // 0=Both, 1=Layer 1
            const bool g_bColorOverlayLayer1 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 1;
            const bool g_bColorOverlayMaskLayer1 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 1;
            color.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer1 ? tintMask1 : 1.0) * float(g_bColorOverlayLayer1)));
        #endif

        float flVertexColorMask = float(g_nVertexColorMode1 != 2) * mix(height.g, 1.0, g_nVertexColorMode1 == 0);
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask);
    #else
        // note: g_nVertexColorMode ignored in non-blend
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, height.g);
    #endif

    // Blending
#if (GameVfx_csgo_environment_blend != 0)
    vec4 color2 = texture(g_tColor2, vTexCoord2.xy);
    vec4 height2 = texture(g_tHeight2, vTexCoord2.xy);
    vec4 normal2 = texture(g_tNormal2, vTexCoord2.xy);
    #if (F_DETAIL_NORMAL == 1)
        vec2 detailNormal2 = texture(g_tNormalDetail2, vDetailTexCoords).rg;
    #endif

    float tintMask2 = saturate(((height2.g - 0.5) * g_fTintMaskContrast2 + 0.5) * g_fTintMaskBrightness2);
    height2.a = g_bMetalness2 ? height2.a : 0.0;
    normal2.rg = (normal2.rg - 0.5) * g_fTextureNormalContrast2 + 0.5;
    normal2.b = saturate(((normal2.b - 0.5) * g_fTextureRoughnessContrast2 + 0.5) * g_fTextureRoughnessBrightness2);

    vec4 color2MaybeColorCorrected = mix(color2, vec4((color2 * g_mTextureAdjust2).rgb, color2.a), bvec4(g_bAdjustBaseColor2));
    vec4 color2ColorCorrectedTinted = color2 * g_mTextureColorAdjust2;

    vec3 color2TintedMaybeColorCorrected = mix(color2MaybeColorCorrected.rgb, color2ColorCorrectedTinted.rgb, tintMask2);

    float tintAdjusted2Luma = GetLuma(color2TintedMaybeColorCorrected);

    float tintColorAdjusted2LumaRatio = tintAdjusted2Luma / tintColorNormLuma;
    float tintAdjusted2Luma3x = 3.0 * tintAdjusted2Luma;

    vec3 tintResult2 = saturate(mix(color2TintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjusted2LumaRatio, tintAdjusted2Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask2) * float(g_bModelTint2))));

    color2.rgb = tintResult2;

    #if (F_SHARED_COLOR_OVERLAY == 1)
        // 0=Both, 2=Layer 2
        const bool g_bColorOverlayLayer2 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 2;
        const bool g_bColorOverlayMaskLayer2 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 2;
        color2.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer2 ? tintMask2 : 1.0) * float(g_bColorOverlayLayer2)));
    #endif

#if (F_USE_NEW_BLENDING == 1)
    // New height-band blend: structurally identical to the old path below. Only the blend
    // weight differs (a height-band weight with a per-layer carry instead of the normalized
    // GetBlendWeights). The composite, normals and vertex colour mirror the old path exactly.
    float flVertexColorMask2 = float(g_nVertexColorMode2 != 2) * mix(height2.g, 1.0, g_nVertexColorMode2 == 0);
    color2.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask2);

    float bf2 = saturate(vColorBlendValues.x * 1.1 - 0.05);
    float heightScaled1 = (height.r - g_flHeightMapZeroPoint1) * g_flHeightMapScale1;
    LayerCarry carry2 = GetLayer2WeightAndCarry(bf2, height2.r, heightScaled1, vColorBlendValues.w, mat.GeometricNormal);
    float weight2 = carry2.weight;

    height.r -= g_flHeightMapZeroPoint1;
    height2.r -= g_flHeightMapZeroPoint2;

    color.rgb = CombineColor(color.rgb, color2.rgb, weight2, g_flColorOverlay2, g_flColorReplace2);
    color.a = mix(color.a, color2.a, weight2);
    height = mix(height, height2, weight2);
    normal = mix(normal, normal2, weight2);
    aoLevels = mix(aoLevels, g_vAmbientOcclusionLevels2.xyz, weight2);

    #if (F_ENABLE_LAYER_3 == 1)
        vec4 color3 = texture(g_tColor3, vTexCoord3.xy);
        vec4 height3 = texture(g_tHeight3, vTexCoord3.xy);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3.xy);

        float tintMask3 = saturate(((height3.g - 0.5) * g_fTintMaskContrast3 + 0.5) * g_fTintMaskBrightness3);
        height3.a = g_bMetalness3 ? height3.a : 0.0;
        normal3.rg = (normal3.rg - 0.5) * g_fTextureNormalContrast3 + 0.5;
        normal3.b = saturate(((normal3.b - 0.5) * g_fTextureRoughnessContrast3 + 0.5) * g_fTextureRoughnessBrightness3);

        vec4 color3MaybeColorCorrected = mix(color3, vec4((color3 * g_mTextureAdjust3).rgb, color3.a), bvec4(g_bAdjustBaseColor3));
        vec4 color3ColorCorrectedTinted = color3 * g_mTextureColorAdjust3;
        vec3 color3TintedMaybeColorCorrected = mix(color3MaybeColorCorrected.rgb, color3ColorCorrectedTinted.rgb, tintMask3);
        float tintAdjusted3Luma = GetLuma(color3TintedMaybeColorCorrected);
        float tintColorAdjusted3LumaRatio = tintAdjusted3Luma / tintColorNormLuma;
        float tintAdjusted3Luma3x = 3.0 * tintAdjusted3Luma;
        vec3 tintResult3 = saturate(mix(color3TintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjusted3LumaRatio, tintAdjusted3Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask3) * float(g_bModelTint3))));
        color3.rgb = tintResult3;

        #if (F_SHARED_COLOR_OVERLAY == 1)
            const bool g_bColorOverlayLayer3 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 3;
            const bool g_bColorOverlayMaskLayer3 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 3;
            color3.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer3 ? tintMask3 : 1.0) * float(g_bColorOverlayLayer3)));
        #endif

        float flVertexColorMask3 = float(g_nVertexColorMode3 != 2) * mix(height3.g, 1.0, g_nVertexColorMode3 == 0);
        color3.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask3);

        float bf3 = saturate(vColorBlendValues.y * 1.1 - 0.05);
        float weight3 = GetLayer3Weight(bf3, height3.r, carry2, vColorBlendValues.w, mat.GeometricNormal);

        height3.r -= g_flHeightMapZeroPoint3;

        color.rgb = CombineColor(color.rgb, color3.rgb, weight3, g_flColorOverlay3, g_flColorReplace3);
        color.a = mix(color.a, color3.a, weight3);
        height = mix(height, height3, weight3);
        normal = mix(normal, normal3, weight3);
        aoLevels = mix(aoLevels, g_vAmbientOcclusionLevels3.xyz, weight3);
    #endif

#else // F_USE_NEW_BLENDING == 0 : original height-blend algorithm
    float flVertexColorMask2 = float(g_nVertexColorMode2 != 2) * mix(height2.g, 1.0, g_nVertexColorMode2 == 0);
    color2.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask2);

    height.r -= g_flHeightMapZeroPoint1;
    height2.r -= g_flHeightMapZeroPoint2;

    vec2 weights = GetBlendWeights(
        vec2(height.r, height2.r),
        vec2(g_flHeightMapScale1, g_flHeightMapScale2),
        vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
        vColorBlendValues
    );

    color.rgb = CombineColor(color.rgb, color2.rgb, weights.y, g_flColorOverlay2, g_flColorReplace2);
    color.a = mix(color.a, color2.a, weights.y);
    height = height * weights.x + height2 * weights.y;
    normal = normal * weights.x + normal2 * weights.y;
    aoLevels = aoLevels * weights.x + g_vAmbientOcclusionLevels2.xyz * weights.y;

    #if (F_ENABLE_LAYER_3 == 1)
        vec4 color3 = texture(g_tColor3, vTexCoord3.xy);
        vec4 height3 = texture(g_tHeight3, vTexCoord3.xy);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3.xy);

        float tintMask3 = saturate(((height3.g - 0.5) * g_fTintMaskContrast3 + 0.5) * g_fTintMaskBrightness3);
        height3.a = g_bMetalness3 ? height3.a : 0.0;
        normal3.rg = (normal3.rg - 0.5) * g_fTextureNormalContrast3 + 0.5;
        normal3.b = saturate(((normal3.b - 0.5) * g_fTextureRoughnessContrast3 + 0.5) * g_fTextureRoughnessBrightness3);

        vec4 color3MaybeColorCorrected = mix(color3, vec4((color3 * g_mTextureAdjust3).rgb, color3.a), bvec4(g_bAdjustBaseColor3));
        vec4 color3ColorCorrectedTinted = color3 * g_mTextureColorAdjust3;

        vec3 color3TintedMaybeColorCorrected = mix(color3MaybeColorCorrected.rgb, color3ColorCorrectedTinted.rgb, tintMask3);
        float tintAdjusted3Luma = GetLuma(color3TintedMaybeColorCorrected);

        float tintColorAdjusted3LumaRatio = tintAdjusted3Luma / tintColorNormLuma;
        float tintAdjusted3Luma3x = 3.0 * tintAdjusted3Luma;

        vec3 tintResult3 = saturate(mix(color3TintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjusted3LumaRatio, tintAdjusted3Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask3) * float(g_bModelTint3))));

        color3.rgb = tintResult3;

        #if (F_SHARED_COLOR_OVERLAY == 1)
            // 0=Both, 3=Layer 3
            const bool g_bColorOverlayLayer3 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 3;
            const bool g_bColorOverlayMaskLayer3 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 3;
            color3.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer3 ? tintMask3 : 1.0) * float(g_bColorOverlayLayer3)));
        #endif

        float flVertexColorMask3 = float(g_nVertexColorMode3 != 2) * mix(height3.g, 1.0, g_nVertexColorMode3 == 0);
        color3.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask3);

        height3.r -= g_flHeightMapZeroPoint3;

        vec2 weights3 = GetBlendWeights(
            vec2(max(height.r, height2.r), height3.r),
            vec2(max(g_flHeightMapScale1, g_flHeightMapScale2), g_flHeightMapScale3),
            vec2(max(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2), g_flHeightMapZeroPoint3),
            vec4(vColorBlendValues.y, 0.0, 0.0, vColorBlendValues.w)
        );

        color.rgb = CombineColor(color.rgb, color3.rgb, weights3.y, g_flColorOverlay3, g_flColorReplace3);
        color.a = mix(color.a, color3.a, weights3.y);
        height = height * (1.0 - weights3.y) + height3 * weights3.y;
        normal = normal * (1.0 - weights3.y) + normal3 * weights3.y;
        aoLevels = aoLevels * (1.0 - weights3.y) + g_vAmbientOcclusionLevels3.xyz * weights3.y;
    #endif
#endif // F_USE_NEW_BLENDING
#endif

    mat.Albedo = color.rgb;
    mat.AmbientOcclusion = color.a;

    // Alpha test
    #if (F_ALPHA_TEST == 1)
        mat.AmbientOcclusion = height.b;
        mat.Opacity = AlphaTestAntiAliasing(color.a, vTexCoord.xy);
    #endif

    mat.AmbientOcclusion = mix(aoLevels.x, aoLevels.z, pow(max(mat.AmbientOcclusion, 0.0), max(aoLevels.y, 0.001)));

    // Normals and Roughness
    mat.NormalMap = DecodeHemiOctahedronNormal(normal.rg);
    mat.RoughnessTex = normal.bb;

    // Detail texture
    #if (F_DETAIL_NORMAL == 1)
        // this is wrong. todo
        vec2 detailNormal = texture(g_tNormalDetail1, vDetailTexCoords).rg;
        mat.NormalMap.xy = (mat.NormalMap.xy + detailNormal) * vec2(0.5);
    #endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Height = height.r;
    mat.Metalness = height.a;

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    const vec3 F0 = vec3(0.04);
	mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

// MAIN

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    // Get material
    MaterialProperties_t mat = GetMaterial(vertexNormal);

    LightingTerms_t lighting = CalculateLighting(mat);

    // Combining pass
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor = vec4(combinedLighting, mat.Opacity);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor1, vTexCoord.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(vTintColor_ModelAmount.rgb, vVertexColor_Alpha.a);
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor.rgb = vVertexColor_Alpha.rgb;
    }
#if (GameVfx_csgo_environment_blend != 0)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vColorBlendValues.rgb;
    }
#endif
}
