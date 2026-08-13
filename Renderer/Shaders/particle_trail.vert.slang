#version 460

// Locations and names pair with the VertexFormat in RenderTrails.cs.
layout (location = 0) in vec3 aVertexPosition;
layout (location = 1) in vec4 aVertexColor;
layout (location = 2) in vec2 aTexCoords;
// The frame after the one aTexCoords addresses, and how far between the two this particle sits.
layout (location = 3) in vec2 aTexCoordsNextFrame;
layout (location = 4) in float aFrameBlend;

#include "common/ViewConstants.slang"
#include "common/utils.slang"

// m_bGammaCorrectVertexColors, m_vecColorScale and m_flDepthBias, shared with the sprite renderer.
uniform bool uGammaCorrectVertexColors = true;
uniform vec3 uColorScale = vec3(1.0);
uniform float uDepthBias = 0.0;

out vec2 vTexCoordOut;
out vec2 vTexCoordNextFrameOut;
out float vFrameBlend;
out vec4 vColor;
out vec3 vFragPosition;

void main(void) {
    vColor = aVertexColor;

    if (uGammaCorrectVertexColors)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

    vColor.rgb *= uColorScale;

    vTexCoordOut = aTexCoords;
    vTexCoordNextFrameOut = aTexCoordsNextFrame;
    vFrameBlend = aFrameBlend;
    vFragPosition = aVertexPosition;
    gl_Position = g_matWorldToProjection * vec4(aVertexPosition, 1.0);

    if (uDepthBias != 0.0)
    {
        vec4 biased = g_matWorldToProjection * vec4(aVertexPosition + (g_vCameraDirWs * uDepthBias), 1.0);

        // Rehomogenised against the unbiased w so only depth moves, then floored off the near plane
        gl_Position.z = max(gl_Position.w * (biased.z / biased.w), min(0.001, gl_Position.z));
    }
}
