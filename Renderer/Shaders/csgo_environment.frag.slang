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
uniform int g_nColorCorrectionMode1 = 0; // 1 = adjust the base colour through g_mTextureAdjust1
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

in vec4 vColorBlendValues;

// Material 2
#if (GameVfx_csgo_environment_blend != 0)
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
    uniform int g_nColorCorrectionMode2 = 0;
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
        uniform int g_nColorCorrectionMode3 = 0;
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

    // Colour composite parameters, shared by both blend paths. At these defaults the
    // composite is a plain lerp toward the upper layer.
    uniform float g_flColorOverlay2 = 0.0;
    uniform float g_flColorReplace2 = 1.0;
    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flColorOverlay3 = 0.0;
        uniform float g_flColorReplace3 = 1.0;
    #endif

    // Mod2x-overlays the gamma-encoded upper layer onto the lower one, then replaces toward it.
    // overlaySource is the upper layer before its vertex colour is applied, which is what the
    // overlay term is built from.
    vec3 CombineColor(vec3 a, vec3 b, vec3 overlaySource, float weight, float overlay, float replace)
    {
        vec3 overlaid = a;

        if (overlay > 0.0)
        {
            overlaid *= mix(vec3(1.0), SrgbLinearToGamma(saturate(overlaySource)) * 2.0, weight * overlay);
        }

        return mix(overlaid, b, weight * replace);
    }

    // Picks the height-band blend below over the normalized two-height blend in GetBlendWeights.
    uniform int F_USE_NEW_BLENDING;
    #define bUseNewBlending (F_USE_NEW_BLENDING == 1)

    // Height-band blend. Every layer above the first gets a single [0, 1] weight from a
    // height difference measured against the layers below it, which is carried upward.
    uniform float g_flUnderlyingHeightMapInfluence2 = 1.0;
    uniform float g_flMaskWithHeight2 = 0.0;

    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flUnderlyingHeightMapInfluence3 = 1.0;
        uniform float g_flMaskWithHeight3 = 0.0;
    #endif

    struct LayerCarry
    {
        float weight;       // this layer's [0, 1] weight
        float signedRaw;    // 2*bf-1, clamped to >= 0 before the layer above reads it
        float signedCarry;  // signedRaw scaled by weight; read only where the layer above
                            // sets UnderlyingHeightMapInfluence below 1
        float underHeight;  // height the layer above measures its difference against
    };

    float BlendBandWeight(float difference, float bf, float rawHeight, float softness, float maskWithHeight)
    {
        softness = max(softness, 1e-4);

        float quarter = softness * 0.25;
        float edgeDenom = 0.02 + quarter;

        float crossfade = saturate(0.5 + difference / (2.0 * softness));
        float edgeHi = saturate((saturate(bf + difference * 0.1 + 0.2) - (0.98 - quarter)) / edgeDenom);
        crossfade = mix(crossfade, 1.0, edgeHi);
        float edgeLo = saturate((edgeDenom - saturate(bf + difference * 0.05 - 0.05)) / edgeDenom);
        crossfade = mix(crossfade, 0.0, edgeLo);

        return saturate(crossfade * mix(0.5, rawHeight, maskWithHeight) * 2.0);
    }

    // Weighs one layer against everything below it. Layer 2 passes a seed carry holding
    // layer 1's scaled height, layer 3 passes what layer 2 returned.
    LayerCarry BlendLayer(LayerCarry below, float bf, float rawHeight, float zeroPoint, float scale,
                          float softness, float underlyingInfluence, float maskWithHeight)
    {
        LayerCarry o;
        o.weight = 0.0;
        o.signedRaw = 0.0;
        o.signedCarry = 0.0;
        o.underHeight = below.underHeight;

        if (bf <= 0.0)
        {
            return o;
        }

        float signedFactor = 2.0 * bf - 1.0;
        float heightPlusSigned = ((rawHeight - zeroPoint) * scale) + signedFactor + max(below.signedRaw, 0.0);
        float difference = heightPlusSigned - mix(below.signedCarry, below.underHeight, underlyingInfluence);

        o.weight = BlendBandWeight(difference, bf, rawHeight, softness, maskWithHeight);
        o.signedRaw = signedFactor;

        // The weight-scaled carry and the height it is measured against only propagate
        // once the layer is actually present; signedRaw propagates regardless.
        if (o.weight > 0.05)
        {
            o.signedCarry = signedFactor * o.weight;
            o.underHeight = mix(below.underHeight, max(below.underHeight, heightPlusSigned), o.weight);
        }

        return o;
    }

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
#include "common/wetness.slang"

#include "common/fog.slang"

// The dielectric reflectance the specular colour is built from. Wetness pulls it toward water.
uniform float g_flReflectance = 0.04;

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

    // Where the water line is judged against.
    float rawHeight = height.r;
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

    vec4 colorMaybeColorCorrected = mix(color, vec4((color * g_mTextureAdjust1).rgb, color.a), bvec4(g_nColorCorrectionMode1 == 1));
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

    vec4 color2MaybeColorCorrected = mix(color2, vec4((color2 * g_mTextureAdjust2).rgb, color2.a), bvec4(g_nColorCorrectionMode2 == 1));
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

    float flVertexColorMask2 = float(g_nVertexColorMode2 != 2) * mix(height2.g, 1.0, g_nVertexColorMode2 == 0);
    vec3 color2Overlay = color2.rgb;
    color2.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask2);

    // Both blend paths composite the same way and differ only in how they weigh the layer.
    float baseHeight1 = height.r - g_flHeightMapZeroPoint1;
    float baseHeight2 = height2.r - g_flHeightMapZeroPoint2;

    LayerCarry carry2 = LayerCarry(0.0, 0.0, 0.0, 0.0);
    float weight2;
    float weightBase2;

    if (bUseNewBlending)
    {
        LayerCarry carry1 = LayerCarry(0.0, 0.0, 0.0, baseHeight1 * g_flHeightMapScale1);
        carry2 = BlendLayer(carry1, saturate(vColorBlendValues.x * 1.1 - 0.05), height2.r,
            g_flHeightMapZeroPoint2, g_flHeightMapScale2, vColorBlendValues.w,
            g_flUnderlyingHeightMapInfluence2, g_flMaskWithHeight2);

        weight2 = carry2.weight;
        weightBase2 = 1.0 - weight2;
    }
    else
    {
        vec2 weights = GetBlendWeights(
            vec2(baseHeight1, baseHeight2),
            vec2(g_flHeightMapScale1, g_flHeightMapScale2),
            vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
            vColorBlendValues
        );

        weight2 = weights.y;
        weightBase2 = weights.x;
    }

    rawHeight = (rawHeight * weightBase2) + (height2.r * weight2);

    height.r = baseHeight1;
    height2.r = baseHeight2;

    color.rgb = CombineColor(color.rgb, color2.rgb, color2Overlay, weight2, g_flColorOverlay2, g_flColorReplace2);
    color.a = color.a * weightBase2 + color2.a * weight2;
    height = height * weightBase2 + height2 * weight2;
    normal = normal * weightBase2 + normal2 * weight2;
    aoLevels = aoLevels * weightBase2 + g_vAmbientOcclusionLevels2.xyz * weight2;

    #if (F_ENABLE_LAYER_3 == 1)
        vec4 color3 = texture(g_tColor3, vTexCoord3.xy);
        vec4 height3 = texture(g_tHeight3, vTexCoord3.xy);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3.xy);

        float tintMask3 = saturate(((height3.g - 0.5) * g_fTintMaskContrast3 + 0.5) * g_fTintMaskBrightness3);
        height3.a = g_bMetalness3 ? height3.a : 0.0;
        normal3.rg = (normal3.rg - 0.5) * g_fTextureNormalContrast3 + 0.5;
        normal3.b = saturate(((normal3.b - 0.5) * g_fTextureRoughnessContrast3 + 0.5) * g_fTextureRoughnessBrightness3);

        vec4 color3MaybeColorCorrected = mix(color3, vec4((color3 * g_mTextureAdjust3).rgb, color3.a), bvec4(g_nColorCorrectionMode3 == 1));
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
        vec3 color3Overlay = color3.rgb;
        color3.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask3);

        float baseHeight3 = height3.r - g_flHeightMapZeroPoint3;
        float weight3;

        if (bUseNewBlending)
        {
            LayerCarry carry3 = BlendLayer(carry2, saturate(vColorBlendValues.y * 1.1 - 0.05), height3.r,
                g_flHeightMapZeroPoint3, g_flHeightMapScale3, vColorBlendValues.w,
                g_flUnderlyingHeightMapInfluence3, g_flMaskWithHeight3);

            weight3 = carry3.weight;
        }
        else
        {
            vec2 weights3 = GetBlendWeights(
                vec2(max(height.r, height2.r), baseHeight3),
                vec2(max(g_flHeightMapScale1, g_flHeightMapScale2), g_flHeightMapScale3),
                vec2(max(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2), g_flHeightMapZeroPoint3),
                vec4(vColorBlendValues.y, 0.0, 0.0, vColorBlendValues.w)
            );

            weight3 = weights3.y;
        }

        rawHeight = (rawHeight * (1.0 - weight3)) + (height3.r * weight3);

        height3.r = baseHeight3;

        float weightBase3 = 1.0 - weight3;

        color.rgb = CombineColor(color.rgb, color3.rgb, color3Overlay, weight3, g_flColorOverlay3, g_flColorReplace3);
        color.a = color.a * weightBase3 + color3.a * weight3;
        height = height * weightBase3 + height3 * weight3;
        normal = normal * weightBase3 + normal3 * weight3;
        aoLevels = aoLevels * weightBase3 + g_vAmbientOcclusionLevels3.xyz * weight3;
    #endif
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

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    float reflectance = g_flReflectance;
    ApplyWetness(mat, rawHeight, vColorBlendValues.z, vColorBlendValues.w, reflectance);
    AdjustRoughnessByGeometricNormal(mat);

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

	mat.SpecularColor = mix(vec3(reflectance), mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

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
