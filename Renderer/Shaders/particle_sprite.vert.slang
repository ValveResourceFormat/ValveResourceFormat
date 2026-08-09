#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 3) in vec2 aTexCoords;
layout (location = 4) in vec4 aVertexColor;
// The frame after the one aTexCoords addresses, and how far between the two this particle sits.
layout (location = 5) in vec2 aTexCoordsNextFrame;
layout (location = 6) in float aFrameBlend;
// Each extra layer's own current (xy) and next (zw) sheet frame uvs.
layout (location = 7) in vec4 aLayerUv0;
layout (location = 8) in vec4 aLayerUv1;
layout (location = 9) in vec4 aLayerUv2;
layout (location = 10) in vec4 aLayerUv3;

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
    vLayerUvOut[0] = aLayerUv0;
    vLayerUvOut[1] = aLayerUv1;
    vLayerUvOut[2] = aLayerUv2;
    vLayerUvOut[3] = aLayerUv3;
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
