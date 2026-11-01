#version 460

#include "common/utils.slang"
#include "common/view_constants.slang"
#include "common/fog.slang"
#include "common/depth.slang"
#include "common/spritecard_channels.slang"

#include "common/texturing.slang"
#include "common/lighting_particle.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

#define PARTICLE_OUTPUT_BLEND_MODE_ADD   1
#define PARTICLE_OUTPUT_BLEND_MODE_MOD2X 5
uniform int uBlendMode = 0; // PARTICLE_OUTPUT_BLEND_MODE_ALPHA

// Subtracted from the base texture's decoded frames under modulate-2x, in 1/256 steps per channel.
const vec3 MOD2X_BASE_BIAS = vec3(1.5, 2.5, 1.5) / 256.0;

// The share of the destination each output blend mode covers, per unit of alpha, indexed by mode.
const float OUTPUT_BLEND_MODE_ALPHA_FACTOR[7] = float[](1.0, 0.0, -1.0, 0.5, -0.5, 1.0, 1.0);

// One sampler per texture layer. Layer 0 is the base texture and keeps the plain name.
// One more colour layer than the combo's value, matching Base + Layer1..Layer4.
// A layer that clamps its uvs has a clamp-to-edge sampler object bound to its unit by the renderer.
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

// A sheet of motion vectors, which is not a layer: it composites nothing, and only warps the first two layers
// toward the frames they blend between. The vectors ride green and alpha, signed about a half, and a full
// vector pushes by the scale, in units of the base texture.
#define S_MOTION_VECTORS 0
#if S_MOTION_VECTORS
uniform sampler2D uMotionVectors;
uniform vec2 uMotionVectorScale = vec2(0.0);
in vec4 vMotionVectorUvOut;
#endif

uniform float uOverbrightFactor;
uniform float uAddSelfAmount = 1.0; // 1 + m_flAddSelfAmount
uniform float uDiffuseAmount = 1.0; // m_flDiffuseAmount, scales the scene light
uniform float uSelfIllumAmount = 0.0; // m_flSelfIllumAmount, added unlit
uniform float uDesaturation = 0.0; // m_flDesaturation

// m_bBlendFramesSeq0 / m_bMaxLuminanceBlendingSequence0. Gated on a uniform rather than on vFrameBlendOut
// so that the second fetch sits in draw-uniform control flow.
uniform bool uBlendFrames = true;
uniform bool uMaxLuminanceFrameBlend = false;

#define PARTICLE_LIGHTING_PER_PIXEL (-1)
uniform int uLightingMode = 0; // PARTICLE_LIGHTING_PER_PARTICLE

uniform vec3 uHsvShift = vec3(0.0, 1.0, 1.0);

// The texture alpha is scaled by x and offset by y after the colour stages
uniform vec2 uAlphaRemap = vec2(1.0, 0.0);

// A smoothstep over the texture alpha from x to y, skipped unless x is below y
uniform vec2 uEdgeSoftness = vec2(1000.0, -1000.0);

uniform bool uOutline = false;
uniform vec4 uOutlineColor = vec4(1.0);
uniform vec4 uOutlineRanges = vec4(0.5, 0.7, 0.6, 0.8);

// The feather fades over (min, max) as given. A minimum above the maximum inverts it, keeping only what
// lies within the minimum of the scene, which a reverse depth tested card uses to light nearby surfaces.
uniform bool uFeathering = false;
uniform vec2 uFeatheringRange = vec2(0.0);
uniform float uFeatheringFilter = 1.0;

uniform bool uFogParticles = true;
uniform bool uSaturateColor = true; // m_bSaturateColorPreAlphaBlend
uniform bool uUnsignedBlendAlpha = false; // spritecard shaders with texture layers
uniform bool uClampLayers = false; // each layer's composite is clamped to 1 as well as floored at 0

// The per particle alpha test. Opaque says whether the texture alpha (x) and the vertex alpha (y) are
// replaced by full opacity once the test passes; softness is how far the upper (x) and lower (y) edges of
// the band that passes fade in.
uniform bool uAlphaReference = false;
uniform vec2 uAlphaReferenceOpaque = vec2(0.0);
uniform vec2 uAlphaReferenceSoftness = vec2(0.0);

// The card draws the opaque scene behind it, pushed aside by its texture, rather than the texture itself.
#define S_REFRACT 0
#if S_REFRACT
uniform sampler2D g_tSceneColor; // reserved
uniform float uRefractAmount = 1.0;
uniform int uRefractBlurRadius = 0;
uniform bool uRefractBlurBox = false;
uniform bool uRefractSolid = false;
#endif

// Per-layer mask mode, blend mode and strength (m_nTextureChannels, m_nTextureBlendMode,
// m_flTextureBlend). Every entry below uLayerCount is written by the renderer each draw.
uniform int uLayerCount = 1;
uniform int uLayerChannels[MAX_TEXTURE_LAYERS];
uniform int uLayerBlendMode[MAX_TEXTURE_LAYERS];
uniform float uLayerBlend[MAX_TEXTURE_LAYERS];

// Texture effect modes. NORMALMAP and the motion vectors never reach the chain; the renderer keeps them out.
#define SPRITECARD_TEXTURE_ZOOM             1
#define SPRITECARD_TEXTURE_1D_COLOR_LOOKUP  2
#define SPRITECARD_TEXTURE_UVDISTORTION      3
#define SPRITECARD_TEXTURE_UVDISTORTION_ZOOM 4
uniform int uLayerEffectMode[MAX_TEXTURE_LAYERS];

// m_flDistortion and m_flZoomScale from each layer's m_TextureControls.
uniform float uLayerDistortion[MAX_TEXTURE_LAYERS];
uniform float uLayerZoomScale[MAX_TEXTURE_LAYERS];

// The frame blend of a layer without sheet frames, which then neither cross-fades nor warps.
#define NO_FRAME_BLEND (-1.0)

// Indexed by mask mode: .x premultiplies the ramp's source colour by its alpha, .y switches the lookup
// to read alpha as the luma.
const vec2 MASK_RAMP_FLAGS[15] = vec2[](
    vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 1.0), vec2(0.0, 0.0), vec2(1.0, 0.0),
    vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 1.0), vec2(0.0, 0.0), vec2(1.0, 0.0),
    vec2(1.0, 0.0), vec2(1.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 0.0), vec2(0.0, 0.0));

in vec2 vTexCoordOut;
in vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
in vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
// Each layer's per particle controls. A lookup layer reads its ramp at ((luma + x) * z, y) and blends
// by w. Any other layer zooms x times as fast and weighs its distortion, or else its blend, by y.
flat in vec4 vLayerControlsOut[S_TEXTURE_LAYERS + 1];
in vec2 vFrameBlendOut;
flat in vec2 vAlphaReferenceOut;
// The weight every layer's U carries across a ribbon, divided out here.
in float vUvWeightOut;
in vec2 vCardUvOut;
in vec4 vColor;
in vec3 vFragPosition;
in vec3 vLightOut;

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

// Indexing the flat controls with the loop counter can read zeros for later layers on some drivers, so
// each layer's entry is read by a constant index.
vec4 LayerControls(int layer)
{
    switch (layer)
    {
#if S_TEXTURE_LAYERS > 0
        case 1: return vLayerControlsOut[1];
#endif
#if S_TEXTURE_LAYERS > 1
        case 2: return vLayerControlsOut[2];
#endif
#if S_TEXTURE_LAYERS > 2
        case 3: return vLayerControlsOut[3];
#endif
#if S_TEXTURE_LAYERS > 3
        case 4: return vLayerControlsOut[4];
#endif
    }

    return vLayerControlsOut[0];
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
vec4 DistortPrevious(int previousLayer, vec4 source, vec2 previousUv, float strength)
{
    vec2 offset = (SrgbLinearToGamma(source.rgb).xy - vec2(0.5)) * 2.0;

    return SampleLayer(previousLayer, previousUv - (offset * (strength * 0.125 * source.a)));
}

// A 1D ramp, looked up by the luminance of whatever is beneath this layer. The lookup runs in gamma
// space. The layer's controls place the luminance across the ramp, and alone pick the row, so the row
// is the same over the whole particle.
vec4 SampleGradientRamp(int layer, vec4 previous, int maskMode, vec4 controls)
{
    vec2 flags = MASK_RAMP_FLAGS[maskMode];

    vec4 source = mix(mix(vec4(previous.rgb * previous.aaa, 1.0), previous, flags.x),
                      previous.aaaa, flags.y);

    float luma = GetLuma(SrgbLinearToGamma(source.rgb));
    vec4 ramp = SampleLayer(layer, vec2((luma + controls.x) * controls.z, controls.y));

    // With alpha as the luma the ramp also supplies the alpha; otherwise the layer below keeps its own.
    return mix(vec4(ramp.rgb, previous.a), vec4(ramp.rgb, dot(ramp.rgb, LUMA_601)), flags.y);
}

// Shifts the hue and scales saturation and value, going through HSV and back.
vec3 ApplyHsvShift(vec3 sourceColor)
{
    // The conversion runs on a normalized copy and the magnitude is put back afterwards, so a colour
    // brighter than 1 keeps its overbright rather than being crushed to the top of the value axis.
    float overbright = max(Max3(sourceColor), 1.0);
    vec3 rgb = saturate(sourceColor / overbright);

    float valueMax = Max3(rgb);
    float chroma = valueMax - Min3(rgb);

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

vec4 LayerUvs(int layer)
{
    vec4 uvs = vec4(vTexCoordOut, vTexCoordNextFrameOut);

#if S_TEXTURE_LAYERS > 0
    if (layer > 0)
    {
        uvs = vLayerUvOut[clamp(layer - 1, 0, S_TEXTURE_LAYERS - 1)];
    }
#endif

    uvs.xz /= vUvWeightOut;

    return uvs;
}

// How far between its two sheet frames a layer has crossed. Only the first two layers cross-fade, each by its
// own blend; later layers always show their current frame.
float LayerFrameBlend(int layer)
{
    if (layer == 0)
    {
        return vFrameBlendOut.x;
    }

    if (layer == 1)
    {
        return vFrameBlendOut.y;
    }

    return NO_FRAME_BLEND;
}

#if S_MOTION_VECTORS
// The motion vectors' push at a coordinate.
vec2 SampleFlow(vec2 uv)
{
    return ((texture(uMotionVectors, uv).yw * 2.0) - vec2(1.0)) * uMotionVectorScale;
}
#endif

#if S_REFRACT
// The scene at the fragment, displaced by the texel's red and green signed about a half, as far as its alpha
// and the refract amount take it, then averaged over a square of pixels with gaussian or equal weights.
vec3 SampleRefraction(vec4 texel)
{
    vec2 pixelSize = 1.0 / vec2(textureSize(g_tSceneColor, 0));
    vec2 uv = (gl_FragCoord.xy * pixelSize) - (((texel.xy - vec2(0.5)) * 2.0) * (uRefractAmount * texel.a));

    if (uRefractBlurRadius <= 0)
    {
        return textureLod(g_tSceneColor, uv, 0.0).rgb;
    }

    float radius = float(uRefractBlurRadius);
    float sigma = radius * 0.5;
    vec3 sum = vec3(0.0);
    float weights = 0.0;

    for (int y = -uRefractBlurRadius; y <= uRefractBlurRadius; ++y)
    {
        for (int x = -uRefractBlurRadius; x <= uRefractBlurRadius; ++x)
        {
            vec2 offset = vec2(x, y);
            vec2 normalized = offset / radius;
            float weight = uRefractBlurBox ? 1.0 : exp(-dot(normalized, normalized) / (2.0 * sigma * sigma));

            sum += textureLod(g_tSceneColor, uv + (offset * pixelSize), 0.0).rgb * weight;
            weights += weight;
        }
    }

    return sum / weights;
}
#endif

// 0 below the edge and 1 above it, rising over width when that is positive.
float RampAcross(float distance, float width)
{
    return width > 0.0 ? saturate(distance / width) : float(distance > 0.0);
}

// The alpha of a card that passes where its texture alpha lies above the particle's threshold, and below the
// threshold plus the window.
float AlphaReference(float textureAlpha, float vertexAlpha)
{
    float threshold = vAlphaReferenceOut.x;
    float window = vAlphaReferenceOut.y;

    float band = min(
        RampAcross(textureAlpha - threshold, uAlphaReferenceSoftness.y),
        RampAcross(threshold + window - textureAlpha, uAlphaReferenceSoftness.x));

    return max(uAlphaReferenceOpaque.x, textureAlpha) * band * max(uAlphaReferenceOpaque.y, vertexAlpha);
}

// The push of the current frame toward the next in xy, and of the next back toward the current in zw.
vec4 MotionFlows()
{
#if S_MOTION_VECTORS
    if (uBlendFrames)
    {
        return vec4(SampleFlow(vMotionVectorUvOut.xy), SampleFlow(vMotionVectorUvOut.zw));
    }
#endif

    return vec4(0.0);
}

// A layer's texel at its current frame, cross-faded toward its next frame when the layer blends frames.
vec4 SampleFrames(int layer, vec2 uv, vec2 uvNext, float frameBlend)
{
    // Modulate-2x pulls both base frames down by a fixed offset before anything else reads them;
    // re-samples for zoom or distortion keep the raw texture.
    vec3 mod2xBias = (layer == 0 && uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X) ? MOD2X_BASE_BIAS : vec3(0.0);

    vec4 texel = SampleLayer(layer, uv);
    texel.rgb -= mod2xBias;

    if (!uBlendFrames || layer >= 2)
    {
        return texel;
    }

    vec4 nextFrame = SampleLayer(layer, uvNext);
    nextFrame.rgb -= mod2xBias;

    if (layer == 0 && uMaxLuminanceFrameBlend)
    {
        // Weighting each frame by the square root of its luminance keeps the brighter one
        // dominant through the cross-fade, so a spark does not dim as it crosses a frame.
        float weight = (1.0 - frameBlend) * sqrt(max(0.001, GetLuma(texel.rgb)));
        float nextWeight = frameBlend * sqrt(max(0.001, GetLuma(nextFrame.rgb)));

        return ((texel * weight) + (nextFrame * nextWeight)) / (weight + nextWeight);
    }

    return mix(texel, nextFrame, frameBlend);
}

// Folds a layer into the accumulator by its mask mode, blend mode and strength.
vec4 FoldLayer(vec4 accum, vec4 layer, int index, float blendWeight)
{
    int maskMode = uLayerChannels[index];
    int blendMode = uLayerBlendMode[index];
    float sub = float(blendMode == SPRITECARD_TEXTURE_BLEND_SUBTRACT);

    vec4 masked = ApplyChannelMask(accum, layer, maskMode, sub);
    vec4 blended = ApplyLayerBlend(accum, masked, blendMode);

    // m_flTextureBlend fades the layer's contribution back toward what was already there.
    vec4 faded = mix(accum, blended, uLayerBlend[index] * blendWeight);

    return uClampLayers ? saturate(faded) : max(faded, vec4(0.0));
}

// Each layer is folded into an accumulator seeded to white. Layer 0 is the base texture, so with one
// layer and the default MULTIPLY this collapses to a straight sample.
vec4 CompositeLayers()
{
    vec4 accum = vec4(1.0);
    vec2 previousUv = LayerUvs(0).xy;
    int previousLayer = 0;

    vec4 flows = MotionFlows();
    vec2 flow = flows.xy;
    vec2 flowNext = flows.zw;

    for (int i = 0; i < uLayerCount; ++i)
    {
        int effectMode = uLayerEffectMode[i];

        // The first two layers cross-fade between their own sheet's current and next frame rects; layers
        // whose rects are constant (one-frame sequences, plain textures) mix two identical samples.
        vec4 uvs = LayerUvs(i);
        vec2 layerUv = uvs.xy;
        vec2 layerUvNext = uvs.zw;
        float frameBlend = LayerFrameBlend(i);

        // Only the first two layers warp, each as far as its own frame blend, and a layer without sheet
        // frames stays put. Strictly, when the first layer has no sheet frames the vectors go unsampled
        // and decode from zero, pushing the second layer by a full negative vector; that is not reproduced.
        if (i < 2 && frameBlend > NO_FRAME_BLEND)
        {
            layerUv -= flow * frameBlend;
            layerUvNext += flowNext * (1.0 - frameBlend);
        }

        frameBlend = max(frameBlend, 0.0);

        vec4 layer = SampleFrames(i, layerUv, layerUvNext, frameBlend);

        vec4 controls = LayerControls(i);
        bool distorts = effectMode == SPRITECARD_TEXTURE_UVDISTORTION || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM;

        if (effectMode == SPRITECARD_TEXTURE_ZOOM || effectMode == SPRITECARD_TEXTURE_UVDISTORTION_ZOOM)
        {
            layer = ZoomSample(i, layerUv, uLayerZoomScale[i] * controls.x);
        }

        if (distorts)
        {
            layer = DistortPrevious(previousLayer, layer, previousUv, uLayerDistortion[i] * controls.y);
        }

        previousUv = layerUv;
        previousLayer = i;

        int maskMode = uLayerChannels[i];

        // A distortion layer's weight went into its distortion, so its blend is left as it is
        float blendWeight = distorts ? 1.0 : controls.y;

        if (effectMode == SPRITECARD_TEXTURE_1D_COLOR_LOOKUP)
        {
            // Effects read the layer's own sample on layer 0 and the accumulator on later layers, which
            // keeps this a special case rather than something that folds away.
            layer = SampleGradientRamp(i, i == 0 ? layer : accum, maskMode, controls);
            blendWeight = controls.w;
        }

        accum = FoldLayer(accum, layer, i, blendWeight);
    }

    return accum;
}

// The scene behind the card when it refracts, otherwise the card's own colour.
vec3 ResolveCardColor(vec4 composite)
{
#if S_REFRACT
    return SampleRefraction(composite);
#else
    return composite.rgb;
#endif
}

// Overlays the outline colour across its alpha band. Solid between Start1 and End0, feathered outside.
void ApplyOutline(float rawAlpha, inout vec3 color, inout float alpha)
{
    if (uAlphaReference || !uOutline || rawAlpha < uOutlineRanges.x || rawAlpha > uOutlineRanges.w)
    {
        return;
    }

    float outlineRamp = rawAlpha <= uOutlineRanges.z
        ? smoothstep(uOutlineRanges.x, uOutlineRanges.z, rawAlpha)
        : 1.0 - smoothstep(uOutlineRanges.y, uOutlineRanges.w, rawAlpha);

    color = mix(color, uOutlineColor.rgb, outlineRamp);
    alpha = mix(alpha, uOutlineColor.a, outlineRamp);
}

// The alpha after the remap and the particle's own alpha.
float ApplyAlphaRemap(float alpha)
{
    // The offset is not scaled, so only a remap whose zero point is 0 sends it to 0. The result is only
    // saturated when frames blend.
    alpha = alpha * uAlphaRemap.x + uAlphaRemap.y;

    if (uBlendFrames)
    {
        alpha = saturate(alpha);
    }

    return uAlphaReference ? AlphaReference(alpha, vColor.a) : alpha * vColor.a;
}

// Fades the card out where it comes within the feather range of the scene behind it.
float ApplyFeathering(float alpha)
{
    if (!uFeathering)
    {
        return alpha;
    }

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

    float pastMin = distance(vFragPosition, scenePosition) - offset - uFeatheringRange.x;
    float range = uFeatheringRange.y - uFeatheringRange.x;

    // An empty range is a hard cut at the minimum distance
    return alpha * (range != 0.0 ? saturate(pastMin / range) : float(pastMin > 0.0));
}

float ApplyRefractSolid(float alpha)
{
#if S_REFRACT
    if (uRefractSolid)
    {
        return saturate(alpha * 128.0);
    }
#endif

    return alpha;
}

// Modulate-2x is its own output, not an additive one: 0.5 is the neutral that leaves the
// destination untouched, and the vertex colour weights each channel away from grey rather than
// scaling it. The lighting and fog stages are skipped on this path.
vec4 Modulate2xOutput(vec3 color, float alpha)
{
    vec3 mod2xColor = mix(vec3(0.5), mix(vec3(0.5), color * uOverbrightFactor, vColor.rgb), vec3(alpha));

    return vec4(saturate(mod2xColor), alpha);
}

// Lights the colour with the scene light and applies the overbright, self add, saturation and fog stages.
vec3 ShadeColor(vec3 color, float blendAlphaFactor)
{
    vec3 litColor = color * uSelfIllumAmount;

    if (uDiffuseAmount != 0.0)
    {
        vec3 light = vLightOut;

        // TODO: Whether per pixel lighting tessellates the card rather than lighting its fragments is not
        // known, so it is lit per fragment
        if (uLightingMode == PARTICLE_LIGHTING_PER_PIXEL)
        {
            ParticleLight_t pixelLight = CalculateParticleLighting(vFragPosition, -g_vCameraDirWs, 0.0, false);
            light = pixelLight.Ambient + pixelLight.Direct;
        }

        litColor += color * light * uDiffuseAmount;
    }

    vec3 shaded = uOverbrightFactor * litColor;

    // m_flAddSelfAmount arrives as 1 + amount; over an alpha blend the extra term lays the
    // particle's own colour additively on top.
    shaded *= uAddSelfAmount;

    if (uSaturateColor)
    {
        shaded = saturate(shaded);
    }

    // The fog colour is scaled by the blend mode's alpha factor, so additive output only fades with
    // fog density. ApplyFog is affine in the colour, so fogging black gives the fog colour term.
    if (uFogParticles)
    {
        vec3 fogColor = vec3(0.0);
        vec3 fogWhite = vec3(1.0);
        ApplyFog(fogColor, vFragPosition);
        ApplyFog(fogWhite, vFragPosition);
        shaded = shaded * saturate(fogWhite.g - fogColor.g) + fogColor * blendAlphaFactor;
    }

    return shaded;
}

vec4 BlendedOutput(vec3 color, float alpha, float blendAlphaFactor)
{
    if (uUnsignedBlendAlpha)
    {
        return vec4(color * mix(alpha, 1.0, saturate(-blendAlphaFactor)), alpha * abs(blendAlphaFactor));
    }

    return vec4(color * alpha, alpha * blendAlphaFactor);
}

void main(void) {
    vec4 composite = CompositeLayers();

    vec3 finalColor = vColor.rgb * ResolveCardColor(composite);

    if (uHsvShift != vec3(0.0, 1.0, 1.0))
    {
        finalColor = ApplyHsvShift(finalColor);
    }

    // Both the soft edge and the outline band read the raw texture alpha; only the soft edge's result is
    // carried forward, so an outline lands in the same place whether or not soft edges are on.
    float rawAlpha = composite.a;
    float alpha = rawAlpha;

    if (!uAlphaReference && uEdgeSoftness.x < uEdgeSoftness.y)
    {
        alpha = smoothstep(uEdgeSoftness.x, uEdgeSoftness.y, rawAlpha);
    }

    ApplyOutline(rawAlpha, finalColor, alpha);

    // Pulls the whole card toward its own luminance, after the outline so the overlay desaturates with
    // everything else.
    finalColor = mix(finalColor, vec3(GetLuma(finalColor)), uDesaturation);

    alpha = ApplyAlphaRemap(alpha);
    alpha = ApplyFeathering(alpha);
    alpha = ApplyRefractSolid(alpha);

    float blendAlphaFactor = OUTPUT_BLEND_MODE_ALPHA_FACTOR[clamp(uBlendMode, 0, 6)];

    if (uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
    {
        fragColor = Modulate2xOutput(finalColor, alpha);
    }
    else
    {
        finalColor = ShadeColor(finalColor, blendAlphaFactor);
        fragColor = BlendedOutput(finalColor, alpha, blendAlphaFactor);
    }

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
