#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"
#include "common/depth.slang"
#include "common/spritecard_channels.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

#define PARTICLE_OUTPUT_BLEND_MODE_ADD   1
#define PARTICLE_OUTPUT_BLEND_MODE_MOD2X 5
uniform int uBlendMode = 0; // PARTICLE_OUTPUT_BLEND_MODE_ALPHA

// One sampler per texture layer. Layer 0 is the base texture and keeps the plain name.
// One more colour layer than the combo's value, matching the engine's Base + Layer1..Layer4.
#define S_TEXTURE_LAYERS 0
#define MAX_TEXTURE_LAYERS (S_TEXTURE_LAYERS + 1)
uniform sampler2D uTexture;
#if S_TEXTURE_LAYERS > 0
uniform sampler2D uTextureLayer1;
#endif
#if S_TEXTURE_LAYERS > 1
uniform sampler2D uTextureLayer2;
#endif
#if S_TEXTURE_LAYERS > 2
uniform sampler2D uTextureLayer3;
#endif
#if S_TEXTURE_LAYERS > 3
uniform sampler2D uTextureLayer4;
#endif

uniform float uOverbrightFactor;
uniform float uAddSelfAmount = 1.0; // 1 + m_flAddSelfAmount
uniform float uColorFactor = 1.0; // m_flDiffuseAmount + m_flSelfIllumAmount
uniform float uDesaturation = 0.0; // m_flDesaturation
// m_bSaturateColorPreAlphaBlend, which defaults on. A good deal of shipped content turns it off to let
// the brightness run past 1 into the blend.
uniform bool uSaturateColorPreAlphaBlend = true;

// m_bBlendFramesSeq0 / m_bMaxLuminanceBlendingSequence0. Gated on a uniform rather than on vFrameBlendOut
// so that the second fetch sits in draw-uniform control flow.
uniform bool uBlendFrames = true;
uniform bool uMaxLuminanceFrameBlend = false;

uniform vec3 uHsvShift = vec3(0.0, 1.0, 1.0);
uniform vec2 uAlphaRemapRange = vec2(1.0, 0.0);

uniform bool uOutline = false;
uniform vec4 uOutlineColor = vec4(1.0);
uniform vec4 uOutlineRanges = vec4(0.5, 0.7, 0.6, 0.8);

uniform vec2 uFeatheringRange = vec2(0.0);
uniform float uFeatheringFilter = 1.0;

// Per-layer mask mode, blend mode and strength (m_nTextureChannels, m_nTextureBlendMode,
// m_flTextureBlend). Every entry below uLayerCount is written by the renderer each draw.
uniform int uLayerCount = 1;
uniform int uLayerChannels[MAX_TEXTURE_LAYERS];
uniform int uLayerBlendMode[MAX_TEXTURE_LAYERS];
uniform float uLayerBlend[MAX_TEXTURE_LAYERS];

// SpriteCardTextureType_t, for the values that are effect modes. NORMALMAP and ANIMMOTIONVEC never reach
// the shader: the renderer keeps them out of the colour chain entirely.
#define SPRITECARD_TEXTURE_ZOOM             1
#define SPRITECARD_TEXTURE_1D_COLOR_LOOKUP  2
#define SPRITECARD_TEXTURE_UVDISTORTION      3
#define SPRITECARD_TEXTURE_UVDISTORTION_ZOOM 4
uniform int uLayerEffectMode[MAX_TEXTURE_LAYERS];

// m_flDistortion and m_flZoomScale from each layer's m_TextureControls.
uniform float uLayerDistortion[MAX_TEXTURE_LAYERS];
uniform float uLayerZoomScale[MAX_TEXTURE_LAYERS];


// A constant table from the reference, indexed by mask mode: .x premultiplies the ramp's source colour by
// its alpha, .y switches the lookup to read alpha as the luma.
const vec2 MASK_RAMP_FLAGS[15] = vec2[](
    vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 1.0), vec2(0.0, 0.0), vec2(1.0, 0.0),
    vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 1.0), vec2(0.0, 0.0), vec2(1.0, 0.0),
    vec2(1.0, 0.0), vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 0.0));

in vec2 vTexCoordOut;
in vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
in vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
in float vFrameBlendOut;
in vec2 vCardUvOut;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

// GLSL cannot return an opaque texture, so the per-layer choice happens at the fetch itself.
vec4 SampleLayer(int layer, vec2 uv)
{
    switch (layer)
    {
#if S_TEXTURE_LAYERS > 0
        case 1: return texture(uTextureLayer1, uv);
#endif
#if S_TEXTURE_LAYERS > 1
        case 2: return texture(uTextureLayer2, uv);
#endif
#if S_TEXTURE_LAYERS > 2
        case 3: return texture(uTextureLayer3, uv);
#endif
#if S_TEXTURE_LAYERS > 3
        case 4: return texture(uTextureLayer4, uv);
#endif
    }

    return texture(uTexture, uv);
}

// Two taps of a layer's own texture, at opposite ends of a zoom sweep about the card centre, eased and
// cross-faded. The sweep is what animates; a single tap would only ever be a fixed magnification.
vec4 ZoomSample(int layer, vec2 uv, float rate)
{
    float phase = fract(rate);
    float eased = phase / ((-0.4 * (1.0 - phase)) + 1.0);

    vec2 centred = uv - vec2(0.5);
    vec4 wide = SampleLayer(layer, (centred * mix(4.0, 1.0, eased)) + vec2(0.5));
    vec4 tight = SampleLayer(layer, (centred * mix(1.0, 0.0625, eased)) + vec2(0.5));

    return mix(tight, wide, eased * eased);
}

// A distortion layer never shows its own texture: its red and green drive an offset that the layer
// beneath it is re-sampled through, so what reaches the accumulator is the layer below, warped.
vec4 DistortPrevious(int layer, vec4 source, vec2 previousUv, float strength)
{
    vec2 offset = (SrgbLinearToGamma(source.rgb).xy - vec2(0.5)) * 2.0;

    return SampleLayer(max(layer - 1, 0), previousUv - (offset * (strength * 0.125 * source.a)));
}


// A 1D ramp, looked up by the luminance of whatever is beneath this layer. The lookup runs in gamma
// space, and the ramp is a one-texel-tall gradient the renderer synthesized from m_Gradient.
vec4 SampleGradientRamp(int layer, vec4 previous, int maskMode)
{
    vec2 flags = MASK_RAMP_FLAGS[maskMode];

    vec4 source = mix(mix(vec4(previous.rgb * previous.aaa, 1.0), previous, flags.x),
                      previous.aaaa, flags.y);

    vec4 ramp = SampleLayer(layer, vec2(GetLuma(SrgbLinearToGamma(source.rgb)), 0.5));

    // With alpha as the luma the ramp also supplies the alpha; otherwise the layer below keeps its own.
    return mix(vec4(ramp.rgb, previous.a), vec4(ramp.rgb, dot(ramp.rgb, LUMA_601)), flags.y);
}

// Combine the masked layer into the accumulator.
// Shifts the hue and scales saturation and value, going through HSV and back. Ported from the spritecard
// pixel shader, which rolls its own conversion rather than using a library one.
vec3 ApplyHsvShift(vec3 sourceColor)
{
    // The conversion runs on a normalized copy and the magnitude is put back afterwards, so a colour
    // brighter than 1 keeps its overbright rather than being crushed to the top of the value axis.
    float overbright = max(max(sourceColor.r, max(sourceColor.g, sourceColor.b)), 1.0);
    vec3 rgb = saturate(sourceColor / overbright);

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
        return vec3(satVal.y) * overbright;
    }

    float sector = hue * 6.0;
    float index = floor(sector);
    float f = sector - index;

    float p = satVal.y * (1.0 - satVal.x);
    float q = satVal.y * (1.0 - (satVal.x * f));
    float t = satVal.y * (1.0 - (satVal.x * (1.0 - f)));

    if (index == 0.0) { return vec3(satVal.y, t, p) * overbright; }
    if (index == 1.0) { return vec3(q, satVal.y, p) * overbright; }
    if (index == 2.0) { return vec3(p, satVal.y, t) * overbright; }
    if (index == 3.0) { return vec3(p, q, satVal.y) * overbright; }
    if (index == 4.0) { return vec3(t, p, satVal.y) * overbright; }

    return vec3(satVal.y, p, q) * overbright;
}

void main(void) {
    // Each layer is folded into an accumulator seeded to white. Layer 0 is the base texture, so with one
    // layer and the default MULTIPLY this collapses to a straight sample.
    vec4 accum = vec4(1.0);
    vec2 previousUv = vTexCoordOut;

    for (int i = 0; i < uLayerCount; ++i)
    {
        // Each layer cross-fades between its own sheet's current and next frame rects; layers whose
        // rects are constant (one-frame sequences, plain textures) mix two identical samples.
        vec2 layerUv = vTexCoordOut;
        vec2 layerUvNext = vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
        if (i > 0)
        {
            layerUv = vLayerUvOut[i - 1].xy;
            layerUvNext = vLayerUvOut[i - 1].zw;
        }
#endif

        vec4 layer = SampleLayer(i, layerUv);

        if (uBlendFrames)
        {
            vec4 nextFrame = SampleLayer(i, layerUvNext);

            if (i == 0 && uMaxLuminanceFrameBlend)
            {
                // Weighting each frame by the square root of its luminance keeps the brighter one
                // dominant through the cross-fade, so a spark does not dim as it crosses a frame.
                float weight = (1.0 - vFrameBlendOut) * sqrt(max(0.001, GetLuma(layer.rgb)));
                float nextWeight = vFrameBlendOut * sqrt(max(0.001, GetLuma(nextFrame.rgb)));
                layer = ((layer * weight) + (nextFrame * nextWeight)) / (weight + nextWeight);
            }
            else
            {
                layer = mix(layer, nextFrame, vFrameBlendOut);
            }
        }

        int effectMode = uLayerEffectMode[i];

        if (effectMode == SPRITECARD_TEXTURE_ZOOM || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM)
        {
            layer = ZoomSample(i, layerUv, uLayerZoomScale[i]);
        }

        if (effectMode == SPRITECARD_TEXTURE_UVDISTORTION || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM)
        {
            layer = DistortPrevious(i, layer, previousUv, uLayerDistortion[i]);
        }

        previousUv = layerUv;

        int maskMode = uLayerChannels[i];

        if (effectMode == SPRITECARD_TEXTURE_1D_COLOR_LOOKUP)
        {
            // The colour an effect reads is the layer's own sample on layer 0, but the accumulator
            // afterwards -- a special case in the reference rather than something that folds away.
            layer = SampleGradientRamp(i, i == 0 ? layer : accum, maskMode);
        }

        int blendMode = uLayerBlendMode[i];
        float sub = float(blendMode == SPRITECARD_TEXTURE_BLEND_SUBTRACT);

        vec4 masked = ApplyChannelMask(accum, layer, maskMode, sub);
        vec4 blended = ApplyLayerBlend(accum, masked, blendMode);

        // m_flTextureBlend fades the layer's contribution back toward what was already there.
        accum = max(mix(accum, blended, uLayerBlend[i]), vec4(0.0));
    }

    vec4 color = accum;

    vec3 finalColor = vColor.rgb * color.rgb;

    if (uHsvShift != vec3(0.0, 1.0, 1.0))
    {
        finalColor = ApplyHsvShift(finalColor);
    }

    // Both the remap and the outline band read the raw texture alpha; only the remap's result is carried
    // forward, so an outline lands in the same place whether or not a remap is active.
    float rawAlpha = color.a;
    float alpha = rawAlpha;

    if (uAlphaRemapRange.x < uAlphaRemapRange.y)
    {
        alpha = smoothstep(uAlphaRemapRange.x, uAlphaRemapRange.y, rawAlpha);
    }

    // Overlay the outline colour across its alpha band. Solid between Start1 and End0, feathered outside.
    if (uOutline && rawAlpha >= uOutlineRanges.x && rawAlpha <= uOutlineRanges.w)
    {
        float outlineRamp = rawAlpha <= uOutlineRanges.z
            ? smoothstep(uOutlineRanges.x, uOutlineRanges.z, rawAlpha)
            : 1.0 - smoothstep(uOutlineRanges.y, uOutlineRanges.w, rawAlpha);

        finalColor = mix(finalColor, uOutlineColor.rgb, outlineRamp);
        alpha = mix(alpha, uOutlineColor.a, outlineRamp);
    }

    // Pulls the whole card toward its own luminance, after the outline so the overlay desaturates with
    // everything else.
    finalColor = mix(finalColor, vec3(GetLuma(finalColor)), uDesaturation);

    alpha *= vColor.a;

    if (uFeatheringRange.y > uFeatheringRange.x)
    {
        vec3 toFragment = vFragPosition - g_vCameraPositionWs;
        vec3 scenePosition = GetWorldPositionFromDepth(ivec2(gl_FragCoord.xy), toFragment);

        float spread = 1.0 - alpha;
        float halfWidening = 1.0;

        if (!uBlendFrames)
        {
            spread += length((vCardUvOut * 2.0) - vec2(1.0));
            halfWidening = 0.5;
        }

        float offset = spread * (uFeatheringRange.y * uFeatheringFilter * halfWidening);

        alpha *= saturate((distance(vFragPosition, scenePosition) - offset - uFeatheringRange.x)
            / (uFeatheringRange.y - uFeatheringRange.x));
    }

    if (uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
    {
        // Modulate-2x is its own output, not an additive one: 0.5 is the neutral that leaves the
        // destination untouched, and the vertex colour weights each channel away from grey rather than
        // scaling it. The lighting, overbright and fog stages are all skipped on this path.
        vec3 mod2xColor = mix(vec3(0.5), mix(vec3(0.5), finalColor, vColor.rgb), vec3(alpha));

        fragColor = vec4(saturate(mod2xColor), alpha);
    }
    else
    {
        // spritecard shading: brightness from diffuse+selfillum, optionally clamped before the alpha blend
        finalColor = finalColor * (uOverbrightFactor * uColorFactor);

        if (uSaturateColorPreAlphaBlend)
        {
            finalColor = saturate(finalColor);
        }

        // m_flAddSelfAmount arrives as 1 + amount; over an alpha blend the extra term lays the
        // particle's own colour additively on top. Placed after the saturate to keep the overshoot.
        finalColor *= uAddSelfAmount;

        bool isAdditive = uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_ADD;

        // Additive contributions fade with fog density instead of tinting toward the fog color.
        if (isAdditive)
        {
            finalColor *= GetFogAttenuation(vFragPosition);
        }
        else
        {
            ApplyFog(finalColor, vFragPosition);
        }

        fragColor = vec4(finalColor * alpha, isAdditive ? 0.0 : alpha);
    }

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
