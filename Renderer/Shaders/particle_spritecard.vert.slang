#version 460

#define S_TEXTURE_LAYERS 0

// Where a card's vertices come from.
//   0  the buffer, one vertex per corner, which is what a rope strip hands over
//   1  generated, one set of attributes per particle at divisor 1, for a sprite card
//   2  generated, as 1, but a trail: a colour and a half width per end rather than one of each
//
// The generated paths reuse the same attribute names as the buffer path wherever the data plays the
// same role, because locations are allocated over every name declared in this file, whether or not the
// combo compiling it keeps the declaration.
#define S_PARTICLE_INSTANCED 0

// The corner position, or the card's centre when the corners are generated.
in vec3 vPOSITION;
in vec4 vCOLOR;
in float vFrameBlend;

#if S_PARTICLE_INSTANCED
// Sheet frame rectangles as (min.xy, max.xy), for the frame this particle is on and the one after it.
in vec4 vTEXCOORD;
in vec4 vTEXCOORD1;
#else
in vec2 vTEXCOORD;
// The frame after the one vTEXCOORD addresses, and how far between the two this particle sits.
in vec2 vTEXCOORD1;
#endif

#if S_PARTICLE_INSTANCED == 1
// The card's axes with the radius already folded in, so a corner is centre + u*x + v*y. The basis is
// still built on the CPU; only the expansion happens here.
in vec3 vAxisU;
in vec3 vAxisV;
#elif S_PARTICLE_INSTANCED == 2
// A trail tapers independently at each end, so the half widths ride in w rather than being folded into
// the axis: across in xyz with the head's half width, along and already halved with the tail's.
in vec4 vAxisU;
in vec4 vAxisV;
// The tail's colour; vCOLOR is the head's.
in vec4 vColorTail;
#endif

// Each extra layer resolves its own sheet frame. Generated corners take the rectangles and place them
// here; handed-over vertices arrive already placed, as this frame's uv in xy and the next frame's in zw.
#if S_TEXTURE_LAYERS > 0
in vec4 vLayerUv0;
#if S_PARTICLE_INSTANCED
in vec4 vLayerUvNext0;
#endif
#endif
#if S_TEXTURE_LAYERS > 1
in vec4 vLayerUv1;
#if S_PARTICLE_INSTANCED
in vec4 vLayerUvNext1;
#endif
#endif
#if S_TEXTURE_LAYERS > 2
in vec4 vLayerUv2;
#if S_PARTICLE_INSTANCED
in vec4 vLayerUvNext2;
#endif
#endif
#if S_TEXTURE_LAYERS > 3
in vec4 vLayerUv3;
#if S_PARTICLE_INSTANCED
in vec4 vLayerUvNext3;
#endif
#endif

#include "common/utils.slang"
#include "common/ViewConstants.slang"

// m_bGammaCorrectVertexColors, which defaults on. Particle colours are authored in gamma space; without
// this they are used as though they were already linear. Alpha is left alone.
uniform bool uGammaCorrectVertexColors = true;

// m_vecColorScale, a linear tint applied after the gamma correction above.
uniform vec3 uColorScale = vec3(1.0);

// m_flDepthBias, in world units along the view direction. The card keeps its drawn position and only
// its depth is re-taken from the shifted point, so a positive bias makes the card lose the depth test
// against geometry it is in front of, and a negative one makes it win.
uniform float uDepthBias = 0.0;

#if S_PARTICLE_INSTANCED
// Each layer's uv controls: scale in xy, offset in zw, then the rotation and whether it clamps. One
// value per layer for the whole draw rather than per particle, which is what lets a particle send the
// sheet frame rectangle it is on instead of a placed coordinate per corner.
uniform vec4 uLayerUvScaleOffset[S_TEXTURE_LAYERS + 1];
uniform float uLayerUvRotation[S_TEXTURE_LAYERS + 1];
uniform bool uLayerUvClamp[S_TEXTURE_LAYERS + 1];

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
#endif

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
#if S_TEXTURE_LAYERS > 0
out vec4 vLayerUvOut[S_TEXTURE_LAYERS];
#endif
out float vFrameBlendOut;
out vec2 vCardUvOut;
out vec4 vColor;
out vec3 vFragPosition;

void main(void) {
#if S_PARTICLE_INSTANCED
    // 0 -> (-1,+1), 1 -> (-1,-1), 2 -> (+1,+1), 3 -> (+1,-1). The strip's shared edge, and so the
    // diagonal it splits on, is the one the index buffer used. A sprite card is a parallelogram and does
    // not care, but a trail is a trapezoid wherever its ends taper differently, and there the diagonal
    // decides how the sheet interpolates across it.
    vec2 corner = vec2(
        (float(gl_VertexID / 2) * 2.0) - 1.0,
        1.0 - (float(gl_VertexID % 2) * 2.0));
#endif

#if S_PARTICLE_INSTANCED == 1
    // The card coordinate the CPU used to place: u runs with the corner, v against it.
    vec2 cardUv = vec2((corner.x + 1.0) * 0.5, (1.0 - corner.y) * 0.5);

    vec3 positionWs = vPOSITION + (vAxisU * corner.x) + (vAxisV * corner.y);

    vColor = vCOLOR;
#elif S_PARTICLE_INSTANCED == 2
    vec2 cardUv = (corner * 0.5) + vec2(0.5);

    // The head is the end the ribbon runs towards, which the along axis points away from
    bool isHead = corner.y < 0.0;

    float halfWidth = isHead ? vAxisU.w : vAxisV.w;
    vec3 positionWs = vPOSITION + (vAxisU.xyz * (corner.x * halfWidth)) + (vAxisV.xyz * corner.y);

    vColor = isHead ? vCOLOR : vColorTail;
#else
    vec3 positionWs = vPOSITION;

    vec2 cardUv = vTEXCOORD;

    vColor = vCOLOR;
#endif

    if (uGammaCorrectVertexColors)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

    vColor.rgb *= uColorScale;

#if S_PARTICLE_INSTANCED
    vTexCoordOut = PlaceCorner(cardUv, 0, vTEXCOORD.xy, vTEXCOORD.zw);
    vTexCoordNextFrameOut = PlaceCorner(cardUv, 0, vTEXCOORD1.xy, vTEXCOORD1.zw);
#if S_TEXTURE_LAYERS > 0
    vLayerUvOut[0] = vec4(PlaceCorner(cardUv, 1, vLayerUv0.xy, vLayerUv0.zw),
                          PlaceCorner(cardUv, 1, vLayerUvNext0.xy, vLayerUvNext0.zw));
#endif
#if S_TEXTURE_LAYERS > 1
    vLayerUvOut[1] = vec4(PlaceCorner(cardUv, 2, vLayerUv1.xy, vLayerUv1.zw),
                          PlaceCorner(cardUv, 2, vLayerUvNext1.xy, vLayerUvNext1.zw));
#endif
#if S_TEXTURE_LAYERS > 2
    vLayerUvOut[2] = vec4(PlaceCorner(cardUv, 3, vLayerUv2.xy, vLayerUv2.zw),
                          PlaceCorner(cardUv, 3, vLayerUvNext2.xy, vLayerUvNext2.zw));
#endif
#if S_TEXTURE_LAYERS > 3
    vLayerUvOut[3] = vec4(PlaceCorner(cardUv, 4, vLayerUv3.xy, vLayerUv3.zw),
                          PlaceCorner(cardUv, 4, vLayerUvNext3.xy, vLayerUvNext3.zw));
#endif
#else
    vTexCoordOut = vTEXCOORD;
    vTexCoordNextFrameOut = vTEXCOORD1;
#if S_TEXTURE_LAYERS > 0
    vLayerUvOut[0] = vLayerUv0;
#endif
#if S_TEXTURE_LAYERS > 1
    vLayerUvOut[1] = vLayerUv1;
#endif
#if S_TEXTURE_LAYERS > 2
    vLayerUvOut[2] = vLayerUv2;
#endif
#if S_TEXTURE_LAYERS > 3
    vLayerUvOut[3] = vLayerUv3;
#endif
#endif

    vFrameBlendOut = vFrameBlend;
    vCardUvOut = cardUv;
    vFragPosition = positionWs;
    gl_Position = g_matWorldToProjection * vec4(positionWs, 1.0);

    if (uDepthBias != 0.0)
    {
        vec4 biased = g_matWorldToProjection * vec4(positionWs + (g_vCameraDirWs * uDepthBias), 1.0);

        // Rehomogenised against the unbiased w so only depth moves, then floored off the near plane
        gl_Position.z = max(gl_Position.w * (biased.z / biased.w), min(0.001, gl_Position.z));
    }
}
