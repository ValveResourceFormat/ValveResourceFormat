#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 3) in vec2 aTexCoords;
layout (location = 4) in vec4 aVertexColor;
// The frame after the one aTexCoords addresses, and how far between the two this particle sits.
layout (location = 5) in vec2 aTexCoordsNextFrame;
layout (location = 6) in float aFrameBlend;

#include "common/utils.slang"
#include "common/ViewConstants.slang"

// m_bGammaCorrectVertexColors, which defaults on. Particle colours are authored in gamma space; without
// this they are used as though they were already linear. Alpha is left alone.
uniform bool uGammaCorrectVertexColors = true;

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

    vTexCoordOut = aTexCoords;
    vTexCoordNextFrameOut = aTexCoordsNextFrame;
    vFrameBlend = aFrameBlend;
    vFragPosition = aVertexPosition;
    gl_Position = g_matWorldToProjection * vec4(aVertexPosition, 1.0);
}
