#version 460

// One set of attributes per particle, at divisor 1: the four corners come from gl_VertexID rather than
// from the buffer, so nothing is duplicated per corner. Its own file rather than a combo of
// particle_spritecard, because the two attribute sets together overrun the sixteen a vertex stage has.
#define S_TEXTURE_LAYERS 0

// The card's centre, with the m_flCenterXOffset/YOffset shift already folded in.
in vec3 vPOSITION;
in vec4 vCOLOR;

// The card's axes, with the radius already folded in, so a corner is centre + right*x + up*y. The basis
// itself is still built on the CPU; only the expansion happens here.
in vec3 vRight;
in vec3 vUp;

// Sheet frame rectangles as (min.xy, max.xy), for the frame this particle is on and the one after it.
in vec4 vUvRect;
in vec4 vUvRectNext;
in float vFrameBlend;

// Each layer past the first resolves its own sheet frame, so it carries its own pair of rectangles.
#if S_TEXTURE_LAYERS > 0
in vec4 vLayerRect0;
in vec4 vLayerRectNext0;
#endif
#if S_TEXTURE_LAYERS > 1
in vec4 vLayerRect1;
in vec4 vLayerRectNext1;
#endif
#if S_TEXTURE_LAYERS > 2
in vec4 vLayerRect2;
in vec4 vLayerRectNext2;
#endif
#if S_TEXTURE_LAYERS > 3
in vec4 vLayerRect3;
in vec4 vLayerRectNext3;
#endif

#include "common/utils.slang"
#include "common/ViewConstants.slang"

// m_bGammaCorrectVertexColors, which defaults on. Particle colours are authored in gamma space; without
// this they are used as though they were already linear. Alpha is left alone.
uniform bool uGammaCorrectVertexColors = true;

// m_vecColorScale, a linear tint applied after the gamma correction above.
uniform vec3 uColorScale = vec3(1.0);

// m_flDepthBias, in world units along the view direction. The card keeps its drawn position and only
// its depth is re-taken from the shifted point, so an effect can sit in front of nearby geometry.
uniform float uDepthBias = 0.0;

// Each layer's uv controls: scale in xy, offset in zw, then the rotation and whether it clamps. One
// value per layer for the whole draw, which is what lets a particle send a sheet frame rectangle
// instead of a placed coordinate per corner.
uniform vec4 uLayerUvScaleOffset[S_TEXTURE_LAYERS + 1];
uniform float uLayerUvRotation[S_TEXTURE_LAYERS + 1];
uniform bool uLayerUvClamp[S_TEXTURE_LAYERS + 1];

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
out vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
out float vFrameBlendOut;
out vec4 vColor;
out vec3 vFragPosition;

// Places one card corner in a layer's texture, matching ParticleTextureLayer.PlaceCorner: the card
// coordinate turns about the card centre, is divided by the scale, and lands at the wrapped offset;
// the result then maps into whichever sheet frame rect the layer is showing.
vec2 PlaceCorner(vec2 cardUv, int layer, vec2 rectMin, vec2 rectMax)
{
    vec2 centred = cardUv - vec2(0.5);

    float rotation = uLayerUvRotation[layer];
    float sinRotation = sin(rotation);
    float cosRotation = cos(rotation);

    vec2 rotated = vec2(
        (centred.x * cosRotation) - (centred.y * sinRotation),
        (centred.x * sinRotation) + (centred.y * cosRotation));

    vec4 scaleOffset = uLayerUvScaleOffset[layer];
    vec2 origin = scaleOffset.zw + vec2(0.5);
    vec2 wrapped = origin - floor(origin);

    vec2 placed = (rotated / scaleOffset.xy) + wrapped;

    if (uLayerUvClamp[layer])
    {
        placed = clamp(placed, 0.0, 1.0);
    }

    return rectMin + (placed * (rectMax - rectMin));
}

void main(void) {
    // 0 -> (-1,-1), 1 -> (-1,+1), 2 -> (+1,-1), 3 -> (+1,+1): a strip over the quad. Only the
    // triangulation diagonal differs from the index buffer the cards used to draw through, which a
    // planar parallelogram is indifferent to.
    vec2 corner = (vec2(float(gl_VertexID / 2), float(gl_VertexID % 2)) * 2.0) - 1.0;

    // The card coordinate the CPU used to place: u runs with the corner, v runs against it.
    vec2 cardUv = vec2((corner.x + 1.0) * 0.5, (1.0 - corner.y) * 0.5);

    vec3 positionWs = vPOSITION + (vRight * corner.x) + (vUp * corner.y);

    vColor = vCOLOR;

    if (uGammaCorrectVertexColors)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

    vColor.rgb *= uColorScale;

    vTexCoordOut = PlaceCorner(cardUv, 0, vUvRect.xy, vUvRect.zw);
    vTexCoordNextFrameOut = PlaceCorner(cardUv, 0, vUvRectNext.xy, vUvRectNext.zw);

#if S_TEXTURE_LAYERS > 0
    vLayerUvOut[0] = vec4(PlaceCorner(cardUv, 1, vLayerRect0.xy, vLayerRect0.zw),
                          PlaceCorner(cardUv, 1, vLayerRectNext0.xy, vLayerRectNext0.zw));
#endif
#if S_TEXTURE_LAYERS > 1
    vLayerUvOut[1] = vec4(PlaceCorner(cardUv, 2, vLayerRect1.xy, vLayerRect1.zw),
                          PlaceCorner(cardUv, 2, vLayerRectNext1.xy, vLayerRectNext1.zw));
#endif
#if S_TEXTURE_LAYERS > 2
    vLayerUvOut[2] = vec4(PlaceCorner(cardUv, 3, vLayerRect2.xy, vLayerRect2.zw),
                          PlaceCorner(cardUv, 3, vLayerRectNext2.xy, vLayerRectNext2.zw));
#endif
#if S_TEXTURE_LAYERS > 3
    vLayerUvOut[3] = vec4(PlaceCorner(cardUv, 4, vLayerRect3.xy, vLayerRect3.zw),
                          PlaceCorner(cardUv, 4, vLayerRectNext3.xy, vLayerRectNext3.zw));
#endif

    vFrameBlendOut = vFrameBlend;
    vFragPosition = positionWs;
    gl_Position = g_matWorldToProjection * vec4(positionWs, 1.0);

    if (uDepthBias != 0.0)
    {
        vec4 biased = g_matWorldToProjection * vec4(positionWs + (g_vCameraDirWs * uDepthBias), 1.0);

        // Rehomogenised against the unbiased w so only depth moves, then floored off the near plane
        gl_Position.z = max(gl_Position.w * (biased.z / biased.w), min(0.001, gl_Position.z));
    }
}
