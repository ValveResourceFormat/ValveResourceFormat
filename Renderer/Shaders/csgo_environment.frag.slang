#version 460

#define GameVfx_csgo_environment_blend 0

#include "common/utils.slang"
#include "common/features.slang"
#include "csgo_environment_features.slang"

// MODES
#define S_MODE_DEPTH 0
#define S_MODE_OVERDRAW 0

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


#if (S_MODE_DEPTH == 0) && (S_MODE_OVERDRAW == 0)
    out vec4 outputColor;
#endif

// Material 1
#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference1 = 0.5;
    uniform float g_flAntiAliasedEdgeStrength1 = 1.0;
#endif
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

        uniform vec3 g_vColorOverlayLayerStrengths = vec3(1.0);
        uniform vec3 g_vColorOverlayTintMaskStrengths = vec3(0.0); // -1 .. 1, sign picks mask or inverse
        uniform float g_flOverlayDarknessContrast = 1.0;    // 0.2 .. 2.0
        uniform float g_flOverlayBrightnessContrast = 1.0;  // 0.2 .. 2.0
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
        uniform float g_flBevelStrength2 = 0.0; // * 0.01
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
            uniform float g_flBevelStrength3 = 0.0; // * 0.01
            uniform float g_flBevelCurve3 = 0.0;
            uniform vec2 g_vBevelLayerAmount3 = vec2(1.0);
        #endif
    #endif

    // Colour composite parameters, shared by both blend paths. At these defaults the
    // composite is a plain lerp toward the upper layer.
    uniform float g_flColorOverlay2 = 0.0;
    uniform float g_flColorReplace2 = 1.0;
    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flColorOverlay3 = 0.0;
        uniform float g_flColorReplace3 = 1.0;
    #endif

    float OverlayLayerAmount(float tintMask, float maskStrength, float layerStrength, float share)
    {
        float mask = (maskStrength > 0.0) ? tintMask : 1.0 - tintMask;

        return mix(1.0, mask, abs(maskStrength)) * layerStrength * share;
    }

    // Mod2x-overlays the gamma-encoded upper layer onto the lower one, then replaces toward it.
    vec3 CombineColor(vec3 a, vec3 b, float weight, float overlay, float replace)
    {
        vec3 overlaid = a;

        if (overlay > 0.0)
        {
            overlaid *= mix(vec3(1.0), SrgbLinearToGamma(saturate(b)) * 2.0, weight * overlay);
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
        float difference;   // signed height difference the weight came from; 0 at the seam
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
        o.difference = 0.0;
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
        o.difference = difference;
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

// Hue-preserving recolour: keep the layer's luminance, take the tint's hue, and clamp so a dark
// tint cannot brighten the layer. Used by the model tint and by the border's Colorize mode.
vec3 ColorizeTint(vec3 albedo, vec3 tint, float amount)
{
    vec3 tintNorm = normalize(max(tint, vec3(0.001)));
    float luma = GetLuma(albedo);
    float tinted = min(luma / GetLuma(tintNorm), 3.0 * luma * max(tint.x, max(tint.y, tint.z)));

    return saturate(mix(albedo, tintNorm * tinted, amount));
}

#if (GameVfx_csgo_environment_blend != 0)
    // Where a layer meets everything under it. The border and the bevel are both bands around
    // difference == 0, sized in the same height units the blend weight is measured in.
    struct SeamFrame
    {
        float difference;       // LayerCarry.difference, so 0 exactly on the seam
        float paintMask;        // 0 where the vertex paint is flat, 1 across a transition
        float heightScale;      // seam width, from the height scales of the layers it joins
        float softness;         // blend softness at this pixel
        float weight;           // this layer's blend weight, which sorts lower from upper
        float maskWithHeight;   // this layer's height, folded in by MaskWithHeight
        float tintMask;         // composited tint mask of everything up to this layer
    };

    SeamFrame GetSeamFrame(float difference, float blendFactor, float heightScale, float softness,
                           float weight, float rawHeight, float maskWithHeight, float tintMask)
    {
        SeamFrame seam;
        seam.difference = difference;
        // The paint has to actually cross over here: fade the decoration out in the outer fifth
        // of the gradient, and drop it entirely on the old blend path, which has no difference
        // to measure against.
        seam.paintMask = bUseNewBlending
            ? saturate(blendFactor * 5.0) * saturate((1.0 - blendFactor) * 5.0)
            : 0.0;
        seam.heightScale = 0.5 + 0.5 * heightScale;
        seam.softness = softness;
        seam.weight = weight;
        seam.maskWithHeight = mix(0.5, rawHeight, maskWithHeight);
        seam.tintMask = tintMask;

        return seam;
    }

    // Plateau of width spread around the seam, fading out over softness. The second factor is
    // Valve's guard against a softness authored far wider than the spread it decorates.
    float SeamBand(float difference, float spread, float softness, float spreadParam)
    {
        return (1.0 - smoothstep(spread - softness, spread + softness, abs(difference)))
            * (1.0 - saturate((softness / spreadParam) * 0.01));
    }

    struct BorderParams
    {
        float spread;
        float softness;
        float offset;
        vec3 tint;
        vec2 layerAmount;   // x weighs the border where the lower layer wins, y where this one does
        bool tintMask;
        int blendMode;      // 0=Multiply, 1=Replace, 2=Mod2x, 3=Colorize
        bool roughness;
        float roughnessValue;
    };

    // Recolours both sides of the seam and optionally flattens their roughness. Both layers take
    // the same amount; the blend weight decides which of the two is actually visible.
    void ApplyBlendBorder(inout vec3 lower, inout vec3 upper, inout float lowerRoughness, inout float upperRoughness,
                          SeamFrame seam, BorderParams p)
    {
        if (p.spread <= 0.0)
        {
            return;
        }

        float spread = p.spread * seam.heightScale * seam.paintMask;
        float softness = seam.softness + p.softness;

        float amount = SeamBand(seam.difference + p.offset, spread, softness, p.spread)
            * saturate(seam.paintMask * mix(4.0, 1.0, saturate(softness * 0.5)))
            * mix(saturate(p.layerAmount.x * seam.maskWithHeight * 2.0),
                  saturate(p.layerAmount.y * seam.maskWithHeight * 2.0), seam.weight);

        float mask = p.tintMask ? seam.tintMask : 1.0;
        float weight = amount * mask;

        if (p.blendMode == 3) // Colorize
        {
            lower = mix(lower, ColorizeTint(lower, p.tint, mask), amount);
            upper = mix(upper, ColorizeTint(upper, p.tint, mask), amount);
        }
        else if (p.blendMode == 1) // Replace
        {
            lower = mix(lower, p.tint, weight);
            upper = mix(upper, p.tint, weight);
        }
        else // Multiply, or Mod2x against a doubled tint
        {
            vec3 factor = mix(vec3(1.0), p.blendMode == 2 ? p.tint * 2.0 : p.tint, weight);
            lower *= factor;
            upper *= factor;
        }

        if (p.roughness)
        {
            lowerRoughness = mix(lowerRoughness, p.roughnessValue, weight);
            upperRoughness = mix(upperRoughness, p.roughnessValue, weight);
        }
    }

    struct BevelParams
    {
        float spread;
        float softness;
        float offset;
        float strength;
        float curve;
        vec2 layerAmount;
    };

    // Rounds the seam off. A height ramp is pushed along the geometric normal and differentiated
    // in screen space, which gives the normal the rounded edge would have had, and that is folded
    // into the surface normal over the same band the border uses.
    vec3 BlendBevelNormal(vec3 normal, vec3 geometricNormal, vec3 positionWs, SeamFrame seam, BevelParams p)
    {
        if (p.spread == 0.0 || p.strength == 0.0 || (p.layerAmount.x == 0.0 && p.layerAmount.y == 0.0))
        {
            return normal;
        }

        float spread = p.spread * seam.heightScale * seam.paintMask;
        float softness = (seam.softness + p.softness) * seam.paintMask;
        float difference = seam.difference + p.offset;

        // The band collapses to zero width where the paint does not cross over, so guard the divide
        float ramp = saturate((difference / max(spread + softness, 1e-5)) * 0.5 + 0.5);
        float curve = 1.0 + pow(abs(p.curve), 2.0) * 10.0;
        float height = (p.curve > 0.0) ? pow(ramp, curve) : 1.0 - pow(1.0 - ramp, curve);

        vec3 ramped = positionWs + geometricNormal * height;
        vec3 slope = cross(-dFdyFine(ramped), dFdxFine(ramped));

        if (seam.paintMask == 0.0 || dot(slope, slope) <= 0.0)
        {
            return normal;
        }

        float amount = SeamBand(difference, spread, softness, p.spread)
            * mix(p.layerAmount.x, p.layerAmount.y, seam.weight) * p.strength; // * 100

        vec3 bevel = normalize(mix(geometricNormal, normalize(slope), amount));

        return normalize(normal + bevel - geometricNormal);
    }
#endif

uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/wetness.slang"

#include "common/fog.slang"

#if (S_MODE_DEPTH == 1) || (S_MODE_OVERDRAW == 1)

    #if (S_MODE_OVERDRAW == 1)
        #include "common/quad_overdraw.slang"
    #endif

    #if (F_ALPHA_TEST == 1) || (S_MODE_OVERDRAW == 1)
        out vec4 outputColor;
    #endif

    #if (S_MODE_OVERDRAW == 1) || (F_ALPHA_TEST == 0)
        layout (early_fragment_tests) in;
    #endif

    void main()
    {
        #if (F_ALPHA_TEST == 1)
            outputColor = vec4(0.0, 0.0, 0.0, AlphaTestAntiAliasing(texture(g_tColor1, vTexCoord.xy).a, vTexCoord.xy, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength1));
        #elif (S_MODE_OVERDRAW == 1)
            outputColor = vec4(0.0, 0.0, 0.0, 1.0);
        #endif

        #if (S_MODE_OVERDRAW == 1)
            CountQuadOverdraw();
        #endif
    }

#else

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

    vec4 colorMaybeColorCorrected = mix(color, vec4((color * g_mTextureAdjust1).rgb, color.a), bvec4(g_nColorCorrectionMode1 == 1));
    vec4 colorColorCorrectedTinted = color * g_mTextureColorAdjust1;

    vec3 colorTintedMaybeColorCorrected = mix(colorMaybeColorCorrected.rgb, colorColorCorrectedTinted.rgb, tintMask1);

    color.rgb = ColorizeTint(colorTintedMaybeColorCorrected, vTintColor_ModelAmount.xyz,
        (vTintColor_ModelAmount.w * tintMask1) * float(g_bModelTint1));

    #if (GameVfx_csgo_environment_blend == 0)
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

    color2.rgb = ColorizeTint(color2TintedMaybeColorCorrected, vTintColor_ModelAmount.xyz,
        (vTintColor_ModelAmount.w * tintMask2) * float(g_bModelTint2));

    // Both blend paths composite the same way and differ only in how they weigh the layer.
    float baseHeight1 = height.r - g_flHeightMapZeroPoint1;
    float baseHeight2 = height2.r - g_flHeightMapZeroPoint2;

    LayerCarry carry2 = LayerCarry(0.0, 0.0, 0.0, 0.0, 0.0);
    float weight2;
    float weightBase2;
    float weight3 = 0.0;

    if (bUseNewBlending)
    {
        LayerCarry carry1 = LayerCarry(0.0, 0.0, 0.0, 0.0, baseHeight1 * g_flHeightMapScale1);
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

    // Tint mask of everything composited so far, which the borders mask themselves by.
    float tintMaskBelow = mix(tintMask1, tintMask2, weight2);

    #if (F_BLEND_EFFECTS_2 == 1)
        SeamFrame seam2 = GetSeamFrame(carry2.difference, saturate(vColorBlendValues.x * 1.1 - 0.05),
            max(abs(g_flHeightMapScale1), abs(g_flHeightMapScale2)), vColorBlendValues.w,
            weight2, height2.r, g_flMaskWithHeight2, tintMaskBelow);

        ApplyBlendBorder(color.rgb, color2.rgb, normal.b, normal2.b, seam2,
            BorderParams(g_flBorderSpread2, g_flBorderSoftness2, g_flBorderOffset2, g_vBorderTint2,
                g_vBorderLayerAmount2, g_bBorderTintMask2, F_BORDER_BLEND_MODE_2,
                F_BORDER_ROUGHNESS_2 == 1, g_fBorderRoughness2));
    #endif

    rawHeight = (rawHeight * weightBase2) + (height2.r * weight2);

    height.r = baseHeight1;
    height2.r = baseHeight2;

    color.rgb = CombineColor(color.rgb, color2.rgb, weight2, g_flColorOverlay2, g_flColorReplace2);
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

        color3.rgb = ColorizeTint(color3TintedMaybeColorCorrected, vTintColor_ModelAmount.xyz,
            (vTintColor_ModelAmount.w * tintMask3) * float(g_bModelTint3));

        float baseHeight3 = height3.r - g_flHeightMapZeroPoint3;
        LayerCarry carry3 = LayerCarry(0.0, 0.0, 0.0, 0.0, 0.0);

        if (bUseNewBlending)
        {
            carry3 = BlendLayer(carry2, saturate(vColorBlendValues.y * 1.1 - 0.05), height3.r,
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

        #if (F_BLEND_EFFECTS_3 == 1)
            // Layer 3 decorates its seam against the composite below it, so both its height
            // scale and its tint mask come from layers 1 and 2 blended together.
            SeamFrame seam3 = GetSeamFrame(carry3.difference, saturate(vColorBlendValues.y * 1.1 - 0.05),
                max(abs(mix(g_flHeightMapScale1, g_flHeightMapScale2, weight2)), abs(g_flHeightMapScale3)),
                vColorBlendValues.w, weight3, height3.r, g_flMaskWithHeight3,
                mix(tintMaskBelow, tintMask3, weight3));

            ApplyBlendBorder(color.rgb, color3.rgb, normal.b, normal3.b, seam3,
                BorderParams(g_flBorderSpread3, g_flBorderSoftness3, g_flBorderOffset3, g_vBorderTint3,
                    g_vBorderLayerAmount3, g_bBorderTintMask3, F_BORDER_BLEND_MODE_3,
                    F_BORDER_ROUGHNESS_3 == 1, g_fBorderRoughness3));
        #endif

        rawHeight = (rawHeight * (1.0 - weight3)) + (height3.r * weight3);

        height3.r = baseHeight3;

        float weightBase3 = 1.0 - weight3;

        color.rgb = CombineColor(color.rgb, color3.rgb, weight3, g_flColorOverlay3, g_flColorReplace3);
        color.a = color.a * weightBase3 + color3.a * weight3;
        height = height * weightBase3 + height3 * weight3;
        normal = normal * weightBase3 + normal3 * weight3;
        aoLevels = aoLevels * weightBase3 + g_vAmbientOcclusionLevels3.xyz * weight3;
    #endif

    float share2 = weight2 * (1.0 - weight3);
    float share1 = saturate((1.0 - share2) - weight3);

    #if (F_SHARED_COLOR_OVERLAY == 1)
        float overlayAmount =
            OverlayLayerAmount(tintMask1, g_vColorOverlayTintMaskStrengths.x, g_vColorOverlayLayerStrengths.x, share1)
            + OverlayLayerAmount(tintMask2, g_vColorOverlayTintMaskStrengths.y, g_vColorOverlayLayerStrengths.y, share2);

        #if (F_ENABLE_LAYER_3 == 1)
            overlayAmount += OverlayLayerAmount(tintMask3, g_vColorOverlayTintMaskStrengths.z, g_vColorOverlayLayerStrengths.z, weight3);
        #endif

        if (overlayAmount > 0.0)
        {
            vec3 overlay = texture(g_tSharedColorOverlay, vTexCoord.zw).rgb * 2.0 - 1.0;
            vec3 overlayFactor = max(vec3(0.0), vec3(1.0)
                + (vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast
                + (SafePow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast);

            color.rgb *= mix(vec3(1.0), overlayFactor, overlayAmount);
        }
    #endif

    float vertexColorMasked = tintMask1 * share1 + tintMask2 * share2;
    float vertexColorUnmasked = float(g_nVertexColorMode1 != 0) * share1 + float(g_nVertexColorMode2 != 0) * share2;
    float vertexColorEnabled = float(g_nVertexColorMode1 != 2) * share1 + float(g_nVertexColorMode2 != 2) * share2;

    #if (F_ENABLE_LAYER_3 == 1)
        vertexColorMasked += tintMask3 * weight3;
        vertexColorUnmasked += float(g_nVertexColorMode3 != 0) * weight3;
    #endif

    float vertexColorMask = saturate(vertexColorUnmasked + vertexColorMasked) * vertexColorEnabled;

    #if (F_ENABLE_LAYER_3 == 1)
        vertexColorMask += float(g_nVertexColorMode3 != 2) * weight3;
    #endif

    color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, saturate(vertexColorMask));
#endif

    mat.Albedo = color.rgb;
    mat.AmbientOcclusion = color.a;

    // Alpha test
    #if (F_ALPHA_TEST == 1)
        mat.AmbientOcclusion = height.b;
        mat.Opacity = AlphaTestAntiAliasing(color.a, vTexCoord.xy, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength1);
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

    #if (GameVfx_csgo_environment_blend != 0 && F_BLEND_EFFECTS_2 == 1)
        mat.Normal = BlendBevelNormal(mat.Normal, mat.GeometricNormal, vFragPosition, seam2,
            BevelParams(g_flBevelSpread2, g_flBevelSoftness2, g_flBevelOffset2,
                g_flBevelStrength2, g_flBevelCurve2, g_vBevelLayerAmount2));
    #endif
    #if (GameVfx_csgo_environment_blend != 0 && F_ENABLE_LAYER_3 == 1 && F_BLEND_EFFECTS_3 == 1)
        mat.Normal = BlendBevelNormal(mat.Normal, mat.GeometricNormal, vFragPosition, seam3,
            BevelParams(g_flBevelSpread3, g_flBevelSoftness3, g_flBevelOffset3,
                g_flBevelStrength3, g_flBevelCurve3, g_vBevelLayerAmount3));
    #endif

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

#endif
