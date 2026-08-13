#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;
in vec4 vCOLOR;
// The frame after the one vTEXCOORD addresses, and how far between the two this particle sits.
in vec2 vTEXCOORD1;
in float vFrameBlend;
// Each extra layer's own current (xy) and next (zw) sheet frame uvs.
in vec4 vLayerUv0;
in vec4 vLayerUv1;
in vec4 vLayerUv2;
in vec4 vLayerUv3;

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

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
out vec4 vLayerUvOut[4];
out float vFrameBlendOut;
out vec4 vColor;
out vec3 vFragPosition;

void main(void) {
    vColor = vCOLOR;

    if (uGammaCorrectVertexColors)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

    vColor.rgb *= uColorScale;

    vTexCoordOut = vTEXCOORD;
    vTexCoordNextFrameOut = vTEXCOORD1;
    vLayerUvOut[0] = vLayerUv0;
    vLayerUvOut[1] = vLayerUv1;
    vLayerUvOut[2] = vLayerUv2;
    vLayerUvOut[3] = vLayerUv3;
    vFrameBlendOut = vFrameBlend;
    vFragPosition = vPOSITION;
    gl_Position = g_matWorldToProjection * vec4(vPOSITION, 1.0);

    if (uDepthBias != 0.0)
    {
        vec4 biased = g_matWorldToProjection * vec4(vPOSITION + (g_vCameraDirWs * uDepthBias), 1.0);

        // Rehomogenised against the unbiased w so only depth moves, then floored off the near plane
        gl_Position.z = max(gl_Position.w * (biased.z / biased.w), min(0.001, gl_Position.z));
    }
}
