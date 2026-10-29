#version 460

#ifndef VRF_SURFACE_HEIGHT_BLEND_INCLUDE
#define VRF_SURFACE_HEIGHT_BLEND_INCLUDE

// Blending of height layers by vertex paint, with the seam decoration around where they meet.
// Include after surface_height_layers.slang.

float OverlayLayerAmount(float tintMask, float maskStrength, float layerStrength, float share)
{
    float mask = (maskStrength > 0.0) ? tintMask : 1.0 - tintMask;

    return mix(1.0, mask, abs(maskStrength)) * layerStrength * share;
}

float CombineOcclusion(float lower, float upper, float weight, float combine, float replace)
{
    return mix(lower * mix(1.0, upper, weight * combine), upper, weight * replace);
}

float CombineRoughness(float lower, float upper, float weight, float combine, float replace)
{
    return mix(lower * (1.0 + (upper - lower) * 2.0 * weight * combine), upper, weight * replace);
}

vec3 CombineNormal(vec3 lower, vec3 upper, float weight, float combine, float replace)
{
    vec3 folded = normalize(lower + upper - Up);
    return mix(mix(lower, folded, weight * combine), upper, weight * replace);
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

// Widens blend softness at range. A seam a few pixels wide up close would harden into a jagged edge
// on small height mips, so softness grows with mip level and with how fast height changes
// per pixel. Scaled by the layer's share of the vertex paint.
float BlendSoftnessAtRange(sampler2D heightTexture, vec2 texCoord, float height, float scale, float layerWeight, float strength)
{
    float lod = textureQueryLod(heightTexture, texCoord).x;

    return lod * lod * (0.0125 + fwidth(height * scale)) * 0.02 * strength * layerWeight;
}

// Height-band blend. Every layer above the first gets a single [0, 1] weight from a
// height difference measured against the layers below it, which is carried upward.
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

    // Weight-scaled carry and underHeight only propagate once the layer is present; signedRaw always does.
    if (o.weight > 0.05)
    {
        o.signedCarry = signedFactor * o.weight;
        o.underHeight = mix(below.underHeight, max(below.underHeight, heightPlusSigned), o.weight);
    }

    return o;
}

// The paint transition: 1 across the middle of the gradient, falling to 0 over the outer 1/k of each end
float PaintTransitionMask(float blendFactor, float k)
{
    return saturate(blendFactor * k) * saturate((1.0 - blendFactor) * k);
}

// The upper layer's height range placed between the layers below it by the vertex paint, from the bottom of the
// range with the layer fully under to the top with it fully over.
float GetPaintedAboveOffset(float belowZero, float belowScale, float aboveZero, float aboveScale, float softness,
                            float blendFactor)
{
    float offsetMin = (-belowZero * belowScale - (1.0 - aboveZero) * aboveScale) - softness;
    float offsetMax = (1.0 - belowZero) * belowScale + aboveZero * aboveScale;
    return mix(offsetMin, offsetMax, blendFactor);
}

// The height scale of the upper layer less the softness, kept off zero so it can be divided by
float GetScaleDifference(float heightScale, float softness)
{
    float scaleDifference = heightScale - softness;

    if (scaleDifference == 0.0)
    {
        scaleDifference = 1e-4;
    }

    return scaleDifference;
}

// The blend of an upper layer by overlapping height bands: its height and the heights below it are scaled into
// one range, the upper layer's offset by the vertex paint. Each layer's zero point moves up by half the range
// softness it carries, an upper layer carrying the ones below it too. Layer 1 is scaled by the softness without
// layer 2's share, layer 2 by the full one. Layer 3 is weighed against layers 1 and 2 together.
struct OldBlend_t
{
    float Weight;           // the upper layer's [0, 1] weight
    float Below;            // the lower layers' height above their zero point
    float BelowScale;       // their height scale with its softness
    float BelowOffset;      // where the paint puts them in the range
    float BelowZero;        // layer 1's shifted zero point
    float BelowHeight;      // their height in the range
    float AboveDelta;       // the upper layer's height above its zero point
    float AboveScale;       // its height scale less the softness
    float AboveOffset;      // where the paint puts its range
    float AboveZero;        // its shifted zero point
    float Softness;
    float MaskWithHeight;   // its height folded in by MaskWithHeight
};

OldBlend_t GetOldBlend(vec2 rawHeight, vec2 heightScale, vec2 heightZero, float blendFactor,
                       float softnessBelow, float softness, vec2 zeroShift, float maskWithHeight)
{
    vec2 zero = heightZero + zeroShift;

    float scaleBelow = heightScale.x + softnessBelow;
    float scaleAbove = heightScale.y + softness;
    float scaleDifference = GetScaleDifference(heightScale.y, softness);

    float below = (rawHeight.x - zero.x) * scaleBelow;

    float aboveOffset = GetPaintedAboveOffset(zero.x, scaleBelow, zero.y, scaleAbove, softness, blendFactor);
    float above = (rawHeight.y - zero.y) * scaleDifference + aboveOffset;

    vec2 weights = max(vec2(below, above) - vec2(max(below, above)) + vec2(softness), vec2(0.0));
    float maskHeight = mix(0.5, rawHeight.y, maskWithHeight);

    OldBlend_t blend;
    blend.Weight = saturate(weights.y / (weights.x + 1e-4 + weights.y) * maskHeight * 2.0);
    blend.Below = rawHeight.x - zero.x;
    blend.BelowScale = scaleBelow;
    blend.BelowOffset = 0.0;
    blend.BelowZero = zero.x;
    blend.BelowHeight = below;
    blend.AboveDelta = rawHeight.y - zero.y;
    blend.AboveScale = scaleDifference;
    blend.AboveOffset = aboveOffset;
    blend.AboveZero = zero.y;
    blend.Softness = softness;
    blend.MaskWithHeight = maskHeight;

    return blend;
}

// The band-overlap blend of layer 3. It shares layer 1's range with layer 2, so the three heights
// are measured against each other, while the border and bevel see layers 1 and 2 as one layer mixed by layer 2's
// weight.
OldBlend_t GetOldBlend3(OldBlend_t lower, float rawHeight, float heightScale, float heightZero, float blendFactor,
                        float softness, float zeroShift, float maskWithHeight)
{
    float zero = heightZero + zeroShift;

    float scaleAbove = heightScale + softness;
    float scaleDifference = GetScaleDifference(heightScale, softness);

    float aboveOffset = GetPaintedAboveOffset(lower.BelowZero, lower.BelowScale, zero, scaleAbove, softness, blendFactor);
    float above = (rawHeight - zero) * scaleDifference + aboveOffset;

    float height1 = lower.BelowHeight;
    float height2 = lower.AboveDelta * lower.AboveScale;
    float heightLower = (lower.Weight > 0.05) ? mix(height1, height2, lower.Weight) : height1;

    vec3 weights = max(vec3(height1, height2 + lower.AboveOffset, above) - vec3(max(heightLower, above)) + vec3(softness), vec3(0.0));
    float maskHeight = mix(0.5, rawHeight, maskWithHeight);

    OldBlend_t blend;
    blend.Weight = saturate(weights.z / (weights.x + 1e-4 + weights.y + weights.z) * maskHeight * 2.0);
    blend.Below = mix(lower.Below, lower.AboveDelta, lower.Weight);
    blend.BelowScale = mix(lower.BelowScale, lower.AboveScale, lower.Weight);
    blend.BelowOffset = (lower.Weight > 0.05) ? mix(0.0, lower.AboveOffset, lower.Weight) : 0.0;
    blend.BelowZero = lower.BelowZero;
    blend.BelowHeight = heightLower;
    blend.AboveDelta = rawHeight - zero;
    blend.AboveScale = scaleDifference;
    blend.AboveOffset = aboveOffset;
    blend.AboveZero = zero;
    blend.Softness = softness;
    blend.MaskWithHeight = maskHeight;

    return blend;
}

// The band-overlap blend only lets a layer replace the normal below it where it stands above that.
float GetOldNormalReplaceGate(OldBlend_t blend)
{
    return saturate((blend.AboveDelta * blend.AboveScale + blend.AboveOffset + blend.AboveZero) - blend.BelowHeight);
}

// How much of the border applies at this pixel: the product of how far the upper layer's band grown by the spread
// overlaps the layers below, and how far they grown by it overlap the upper layer. The band only exists where the
// paint is mid-transition.
float GetOldBorderAmount(OldBlend_t blend, float blendFactor, float spread, float offset, float softness)
{
    float paintMask = PaintTransitionMask(blendFactor, 10.0);

    float below = blend.Below * (blend.BelowScale + softness) + blend.BelowOffset;
    float above = blend.AboveDelta * (blend.AboveScale - softness) + blend.AboveOffset - softness;

    float aboveGrown = above + (spread + offset) * paintMask;
    float belowGrown = below + (spread - offset) * paintMask;

    float shift = blend.Softness + softness;
    vec2 aboveOverlap = max(vec2(0.0), vec2(below, aboveGrown) - vec2(max(below, aboveGrown) - shift));
    vec2 belowOverlap = max(vec2(0.0), vec2(belowGrown, above) - vec2(max(belowGrown, above) - shift));

    float aboveShare = aboveOverlap.y / (aboveOverlap.x + 0.001 + aboveOverlap.y);
    float belowBiased = belowOverlap.x + 0.001;
    float belowShare = belowBiased / (belowBiased + belowOverlap.y);

    return aboveShare * belowShare;
}

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
    // The paint has to actually cross over here: fade the decoration out in the outer fifth of the gradient
    seam.paintMask = PaintTransitionMask(blendFactor, 5.0);
    seam.heightScale = 0.5 + 0.5 * heightScale;
    seam.softness = softness;
    seam.weight = weight;
    seam.maskWithHeight = mix(0.5, rawHeight, maskWithHeight);
    seam.tintMask = tintMask;

    return seam;
}

// Plateau of width spread around the seam, fading out over softness.
// The second factor guards against a softness authored far wider than the spread it decorates.
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

// Recolors the two sides of the seam, each by its own amount, and optionally flattens their roughness.
void ApplyBorderSides(inout vec3 lower, inout vec3 upper, inout float lowerRoughness, inout float upperRoughness,
                      vec2 amounts, float mask, BorderParams p)
{
    vec2 weights = amounts * mask;

    if (p.blendMode == 3) // Colorize
    {
        lower = mix(lower, ColorizeTint(lower, p.tint, mask), amounts.x);
        upper = mix(upper, ColorizeTint(upper, p.tint, mask), amounts.y);
    }
    else if (p.blendMode == 1) // Replace
    {
        lower = mix(lower, p.tint, weights.x);
        upper = mix(upper, p.tint, weights.y);
    }
    else // Multiply, or Mod2x against a doubled tint
    {
        vec3 tint = (p.blendMode == 2) ? p.tint * 2.0 : p.tint;

        lower *= mix(vec3(1.0), tint, weights.x);
        upper *= mix(vec3(1.0), tint, weights.y);
    }

    if (p.roughness)
    {
        lowerRoughness = mix(lowerRoughness, p.roughnessValue, weights.x);
        upperRoughness = mix(upperRoughness, p.roughnessValue, weights.y);
    }
}

// Both layers take the same amount; the blend weight decides which of the two is actually visible.
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

    ApplyBorderSides(lower, upper, lowerRoughness, upperRoughness, vec2(amount), mask, p);
}

// The border of the band-overlap blend, where each layer takes its own share of the amount
void ApplyOldBlendBorder(inout vec3 lower, inout vec3 upper, inout float lowerRoughness, inout float upperRoughness,
                         OldBlend_t blend, float blendFactor, float tintMask, BorderParams p)
{
    float amount = GetOldBorderAmount(blend, blendFactor, p.spread, p.offset, p.softness);
    vec2 layerAmount = saturate(p.layerAmount * blend.MaskWithHeight * 2.0);
    float mask = p.tintMask ? tintMask : 1.0;

    ApplyBorderSides(lower, upper, lowerRoughness, upperRoughness, amount * layerAmount, mask, p);
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

// Tilts the geometric normal toward the screen space slope by the amount, and folds the result into the normal
vec3 FoldBevelNormal(vec3 normal, vec3 geometricNormal, vec3 tangent, vec3 bitangent, vec3 slope, float amount)
{
    vec3 bevel = normalize(mix(geometricNormal, normalize(slope), amount));

    // The slope comes out of world space; the normal it folds into has not been there yet
    vec3 bevelTs = vec3(dot(bevel, tangent), dot(bevel, bitangent), dot(bevel, geometricNormal));

    return normalize(normal + bevelTs - Up);
}

// Rounds the seam off: a height ramp pushed along the geometric normal is differentiated in screen
// space to give the rounded edge's normal, folded into the surface normal over the border's band.
vec3 BlendBevelNormal(vec3 normal, vec3 geometricNormal, vec3 tangent, vec3 bitangent, vec3 positionWs,
                      SeamFrame seam, BevelParams p)
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
    float curve = 1.0 + pow2(p.curve) * 10.0;
    float height = (p.curve > 0.0) ? pow(ramp, curve) : 1.0 - pow(1.0 - ramp, curve);

    vec3 ramped = positionWs + geometricNormal * height;
    vec3 slope = cross(-dFdyFine(ramped), dFdxFine(ramped));

    if (seam.paintMask == 0.0 || length_squared(slope) <= 0.0)
    {
        return normal;
    }

    float amount = SeamBand(difference, spread, softness, p.spread)
        * mix(p.layerAmount.x, p.layerAmount.y, seam.weight) * p.strength;

    return FoldBevelNormal(normal, geometricNormal, tangent, bitangent, slope, amount);
}

// The bevel of the band-overlap blend, measured from the same overlap of bands as its border. The ramp
// rises over the lower half of the overlap and falls over the upper half.
vec3 BlendBevelNormalOld(vec3 normal, vec3 geometricNormal, vec3 tangent, vec3 bitangent, vec3 positionWs,
                         OldBlend_t blend, float blendFactor, BevelParams p)
{
    if (p.strength == 0.0 || (p.layerAmount.x == 0.0 && p.layerAmount.y == 0.0))
    {
        return normal;
    }

    float below = blend.Below * (blend.BelowScale + p.softness) + blend.BelowOffset;
    float above = (blend.AboveDelta * (blend.AboveScale - p.softness) + blend.AboveOffset) + p.spread;

    float shift = blend.Softness + p.softness;
    vec2 overlap = max(vec2(0.0), vec2(below, above) - vec2(max(below, above) - shift));
    float share = overlap.y / (overlap.x + 0.001 + overlap.y);

    float curve = max(0.2, p.curve * 2.0);
    float rise = pow(smoothstep(0.0, 0.5, share), curve);
    float fall = pow(smoothstep(1.0, 0.5, share), curve);

    vec3 ramped = positionWs + geometricNormal * (((rise + 1.0) - fall) * p.strength);
    vec3 slope = cross(-dFdyFine(ramped), dFdxFine(ramped));

    if (length(slope) <= 0.0)
    {
        return normal;
    }

    float paintMask = PaintTransitionMask(blendFactor, 10.0);
    float amount = rise * fall * smoothstep(0.0, p.softness, blendFactor) * paintMask
        * mix(1.0, p.layerAmount.x, blend.Weight) * mix(p.layerAmount.y, 1.0, blend.Weight);

    return FoldBevelNormal(normal, geometricNormal, tangent, bitangent, slope, amount);
}

// Fades the paint of a layer by how much the surface faces a direction, between the two bounds. The layer's normal
// map tilts the surface normal by the given strength.
float GetFacingDirectionMask(vec3 direction, vec3 geometricNormal, vec3 layerNormal, float normalMapStrength,
                             float spread, float softness)
{
    // A direction without a z component gets a small one, so the normalize cannot fail
    vec3 safeDirection = normalize(vec3(direction.xy, direction.z + ((direction.z == 0.0) ? 0.0001 : 0.0)));
    float facing = dot(safeDirection, mix(geometricNormal, layerNormal, normalMapStrength));

    return saturate((((facing - 1.0) + spread * 2.0) / (softness * 2.0)) + 0.5);
}

#endif
