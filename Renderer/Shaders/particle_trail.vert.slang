#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;
in vec4 vCOLOR;
// The frame after the one vTEXCOORD addresses, and how far between the two this particle sits.
in vec2 vTEXCOORD1;
in float vFrameBlend;

#include "common/ViewConstants.slang"
#include "common/utils.slang"

// m_bGammaCorrectVertexColors, m_vecColorScale and m_flDepthBias, shared with the sprite renderer.
uniform bool uGammaCorrectVertexColors = true;
uniform vec3 uColorScale = vec3(1.0);
uniform float uDepthBias = 0.0;

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
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
