#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

#define PARTICLE_OUTPUT_BLEND_MODE_ADD   1
#define PARTICLE_OUTPUT_BLEND_MODE_MOD2X 5
uniform int uBlendMode = 0; // PARTICLE_OUTPUT_BLEND_MODE_ALPHA

uniform sampler2D uTexture;
uniform float uOverbrightFactor;
uniform float uColorFactor = 1.0; // m_flDiffuseAmount + m_flSelfIllumAmount
uniform float uDesaturation = 0.0; // m_flDesaturation

uniform vec3 uHsvShift = vec3(0.0, 1.0, 1.0);
uniform vec2 uAlphaRemapRange = vec2(1.0, 0.0);

uniform bool uOutline = false;
uniform vec4 uOutlineColor = vec4(1.0);
uniform vec4 uOutlineRanges = vec4(0.5, 0.7, 0.6, 0.8);

#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB            0
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA           1
#define SPRITECARD_TEXTURE_CHANNEL_MIX_A              2
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A          3
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK  4
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK    5
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA  6
#define SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA     7
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA 8
#define SPRITECARD_TEXTURE_CHANNEL_MIX_R              9
#define SPRITECARD_TEXTURE_CHANNEL_MIX_G              10
#define SPRITECARD_TEXTURE_CHANNEL_MIX_B              11
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA         12
#define SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA         13
#define SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA         14

uniform int uTextureChannels = 1; // MIX_RGBA

#define SPRITECARD_TEXTURE_BLEND_MULTIPLY   0
#define SPRITECARD_TEXTURE_BLEND_MULTIPLY2X 1
#define SPRITECARD_TEXTURE_BLEND_REPLACE    2
#define SPRITECARD_TEXTURE_BLEND_ADD        3
#define SPRITECARD_TEXTURE_BLEND_SUBTRACT   4
#define SPRITECARD_TEXTURE_BLEND_AVERAGE    5
#define SPRITECARD_TEXTURE_BLEND_LUMINANCE  6
// Left at MULTIPLY until the per-layer texture inputs are parsed; with one layer that is the identity.
uniform int uTextureBlendMode = 0; // MULTIPLY

// The mask modes use Rec.601 weights, while the LUMINANCE blend below uses Rec.709 (GetLuma).
const vec3 LUMA_601 = vec3(0.299, 0.587, 0.114);

in vec2 vTexCoordOut;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

// Reduce (accumulator, layer) to a single colour/alpha pair. SUBTRACT zeroes the accumulator's
// contribution in the modes that would otherwise pass it straight through, so that subtracting a
// colour-only layer does not also subtract the alpha it never supplied.
vec4 ApplyChannelMask(vec4 accum, vec4 layer, int maskMode, float sub)
{
    switch (maskMode)
    {
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB:
            return vec4(layer.rgb, mix(accum.a, 0.0, sub));
        case SPRITECARD_TEXTURE_CHANNEL_MIX_A:
            return vec4(mix(accum.rgb, vec3(0.0), sub), layer.a);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A:
            return vec4(layer.rgb, accum.a * layer.a);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK:
            return vec4(mix(accum.rgb, layer.rgb, layer.a), accum.a);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK:
            return vec4(mix(accum.rgb, layer.rgb, dot(layer.rgb, LUMA_601)), accum.a);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA:
            return vec4(layer.rgb, dot(layer.rgb, LUMA_601));
        case SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA:
            return vec4(mix(accum.rgb, vec3(0.0), sub), dot(layer.rgb, LUMA_601));
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA:
            return vec4(accum.rgb, accum.a * dot(layer.rgb, LUMA_601));

        // The single-channel modes keep their own channel and drop the rest under SUBTRACT.
        case SPRITECARD_TEXTURE_CHANNEL_MIX_R:
            return vec4(layer.r, mix(vec3(layer.r, layer.r, accum.a), vec3(0.0), sub));
        case SPRITECARD_TEXTURE_CHANNEL_MIX_G:
            return vec4(mix(layer.g, 0.0, sub), layer.g, mix(vec2(layer.g, accum.a), vec2(0.0), sub));
        case SPRITECARD_TEXTURE_CHANNEL_MIX_B:
            return vec4(mix(vec2(layer.b), vec2(0.0), sub), layer.b, mix(accum.a, 0.0, sub));

        // ...and the *ALPHA variants broadcast their channel into the alpha as well.
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA:
            return vec4(layer.r, mix(vec2(layer.r), vec2(0.0), sub), layer.r);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA:
            return vec4(mix(layer.g, 0.0, sub), layer.g, mix(layer.g, 0.0, sub), layer.g);
        case SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA:
            return vec4(mix(vec2(layer.b), vec2(0.0), sub), layer.b, layer.b);
    }

    return layer; // MIX_RGBA, and anything unrecognised
}

// Combine the masked layer into the accumulator.
vec4 ApplyLayerBlend(vec4 accum, vec4 masked, int blendMode)
{
    switch (blendMode)
    {
        case SPRITECARD_TEXTURE_BLEND_MULTIPLY2X: return accum * masked * 2.0;
        case SPRITECARD_TEXTURE_BLEND_REPLACE:    return masked;
        case SPRITECARD_TEXTURE_BLEND_ADD:        return accum + masked;
        case SPRITECARD_TEXTURE_BLEND_SUBTRACT:   return accum - masked;
        case SPRITECARD_TEXTURE_BLEND_AVERAGE:    return (accum + masked) * 0.5;
        case SPRITECARD_TEXTURE_BLEND_LUMINANCE:  return vec4(accum.rgb * GetLuma(masked.rgb), masked.a);
    }

    return accum * masked; // MULTIPLY, and anything unrecognised
}

// Shifts the hue and scales saturation and value, going through HSV and back. Ported from the spritecard
// pixel shader, which rolls its own conversion rather than using a library one.
vec3 ApplyHsvShift(vec3 rgb)
{
    float valueMax = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = valueMax - min(rgb.r, min(rgb.g, rgb.b));

    vec3 hsv = vec3(0.0, 0.0, valueMax);

    if (chroma != 0.0)
    {
        vec3 distance = (vec3(valueMax) - rgb) / vec3(chroma);
        vec3 delta = distance - distance.zxy;

        // Which sixth of the hue wheel the largest channel puts us in.
        if (rgb.r >= valueMax)      { hsv = vec3(delta.z,       chroma / valueMax, valueMax); }
        else if (rgb.g >= valueMax) { hsv = vec3(delta.x + 2.0, chroma / valueMax, valueMax); }
        else                        { hsv = vec3(delta.y + 4.0, chroma / valueMax, valueMax); }

        hsv.x = fract(hsv.x * (1.0 / 6.0));
    }

    float hue = fract(hsv.x + uHsvShift.x);
    vec2 satVal = saturate(hsv.yz * uHsvShift.yz);

    if (satVal.x == 0.0)
    {
        return vec3(satVal.y);
    }

    float sector = hue * 6.0;
    float index = floor(sector);
    float f = sector - index;

    float p = satVal.y * (1.0 - satVal.x);
    float q = satVal.y * (1.0 - (satVal.x * f));
    float t = satVal.y * (1.0 - (satVal.x * (1.0 - f)));

    if (index == 0.0) { return vec3(satVal.y, t, p); }
    if (index == 1.0) { return vec3(q, satVal.y, p); }
    if (index == 2.0) { return vec3(p, satVal.y, t); }
    if (index == 3.0) { return vec3(p, q, satVal.y); }
    if (index == 4.0) { return vec3(t, p, satVal.y); }

    return vec3(satVal.y, p, q);
}

void main(void) {
    // Layer 0 is the base texture, folded into an accumulator of vec4(1.0). Extra layers will repeat
    // this block with their own mask, blend and uv.
    vec4 accum = vec4(1.0);

    float sub = float(uTextureBlendMode == SPRITECARD_TEXTURE_BLEND_SUBTRACT);
    vec4 layer = texture(uTexture, vTexCoordOut);
    vec4 masked = ApplyChannelMask(accum, layer, uTextureChannels, sub);
    accum = max(ApplyLayerBlend(accum, masked, uTextureBlendMode), vec4(0.0));

    vec4 color = accum;

    vec3 finalColor = vColor.rgb * color.rgb;

    // Texture alpha remap.
    float alpha = color.a;

    if (uAlphaRemapRange.x < uAlphaRemapRange.y)
    {
        alpha = smoothstep(uAlphaRemapRange.x, uAlphaRemapRange.y, alpha);
    }

    // Overlay the outline colour across its alpha band. Solid between Start1 and End0, feathered outside;
    // the second smoothstep runs with its edges swapped so that it falls rather than rises.
    if (uOutline && alpha >= uOutlineRanges.x && alpha <= uOutlineRanges.w)
    {
        float outlineRamp = alpha <= uOutlineRanges.z
            ? smoothstep(uOutlineRanges.x, uOutlineRanges.z, alpha)
            : smoothstep(uOutlineRanges.w, uOutlineRanges.y, alpha);

        finalColor = mix(finalColor, uOutlineColor.rgb, outlineRamp);
        alpha = mix(alpha, uOutlineColor.a, outlineRamp);
    }

    if (uHsvShift != vec3(0.0, 1.0, 1.0))
    {
        finalColor = ApplyHsvShift(finalColor);
    }

    // Pulls the whole card toward its own luminance. Sits after the outline, so the overlay desaturates
    // with everything else, and before the brightness below.
    finalColor = mix(finalColor, vec3(GetLuma(finalColor)), uDesaturation);

    // spritecard shading: brightness from diffuse+selfillum, saturated before the alpha blend. Applied
    // after the outline so the overlay colour is lit the same way the sample is.
    finalColor = saturate(finalColor * (uOverbrightFactor * uColorFactor));

    alpha *= vColor.a;

    // MOD2X composites like ADD. The modulate-2x in spritecard is a texture combiner inside the layer
    // chain (SPRITECARD_TEXTURE_BLEND_MULTIPLY2X above), not a framebuffer op: every blend state the
    // shader ships with is an Add with a One destination, so it never scales what is behind it.
    bool isAdditive = uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_ADD
        || uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X;

    // Additive contributions fade with fog density instead of tinting toward the fog color.
    if (isAdditive)
    {
        finalColor *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(finalColor, vFragPosition);
    }

    // Premultiplied alpha; additive keeps the color term and zeroes the blend weight.
    fragColor = vec4(finalColor * alpha, isAdditive ? 0.0 : alpha);

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
