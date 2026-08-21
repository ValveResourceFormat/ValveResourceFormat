#version 460

// One set of attributes per particle, at divisor 1: the four corners come from gl_VertexID rather than
// from the buffer. Its own file rather than a combo of particle_spritecard_instanced, because a trail is
// a trapezoid with a colour and a half width per end, where a sprite card is a parallelogram with one of
// each, and the two attribute sets together overrun the sixteen a vertex stage has.
#define S_TEXTURE_LAYERS 0

// The centre of the ribbon, already slid along the motion axis by m_flForwardShift.
in vec3 vPOSITION;

// The head colour; the tail end of the ribbon takes vColorTail instead.
in vec4 vCOLOR;
in vec4 vColorTail;

// The ribbon's across axis in xyz, unit length, with the head's half width in w.
in vec4 vWidthAxis;
// The ribbon's along axis in xyz, already scaled to half the length, with the tail's half width in w.
in vec4 vLengthAxis;

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
#include "common/spritecard_uv.slang"

// m_bGammaCorrectVertexColors, which defaults on. Particle colours are authored in gamma space; without
// this they are used as though they were already linear. Alpha is left alone.
uniform bool uGammaCorrectVertexColors = true;

// m_vecColorScale, a linear tint applied after the gamma correction above.
uniform vec3 uColorScale = vec3(1.0);

// m_flDepthBias, in world units along the view direction. The card keeps its drawn position and only
// its depth is re-taken from the shifted point, so an effect can sit in front of nearby geometry.
uniform float uDepthBias = 0.0;

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
out vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
out float vFrameBlendOut;
out vec4 vColor;
out vec3 vFragPosition;

void main(void) {
    // 0 -> (-1,+1), 1 -> (-1,-1), 2 -> (+1,+1), 3 -> (+1,-1). The strip's shared edge, and so its
    // triangulation diagonal, is the one the index buffer used: a trail is a trapezoid whenever its ends
    // taper differently, and there the diagonal decides how the sheet interpolates across it.
    vec2 corner = vec2(
        (float(gl_VertexID / 2) * 2.0) - 1.0,
        1.0 - (float(gl_VertexID % 2) * 2.0));

    vec2 cardUv = (corner * 0.5) + vec2(0.5);

    // The head is the end the ribbon runs towards, which is where the length axis points away from
    bool isHead = corner.y < 0.0;

    float halfWidth = isHead ? vWidthAxis.w : vLengthAxis.w;
    vec3 positionWs = vPOSITION + (vWidthAxis.xyz * (corner.x * halfWidth)) + (vLengthAxis.xyz * corner.y);

    vColor = isHead ? vCOLOR : vColorTail;

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
