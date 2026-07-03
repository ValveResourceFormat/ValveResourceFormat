#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

layout (location = 0) in vec3 aVertexPosition;
layout (location = 1) in vec3 aVertexNormal;
layout (location = 3) in vec2 aTexCoords;
layout (location = 4) in vec4 aVertexColor;

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    uniform uint uLightProbeIndex = 0u;
    flat out uint g_nVisibleLPV;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vColorOut;

void main(void)
{
    vFragPosition = aVertexPosition;
    vNormalOut = aVertexNormal;
    vTexCoordOut = aTexCoords;

    // Per-node colour (COLOR stream, whole-cable tint already folded in). The CPU packs the authored
    // gamma-space colour raw into the byte stream, so one decode here nets the final colour. The alpha
    // rides along but the fragment stage does not read it.
    vColorOut = vec4(SrgbGammaToLinear(aVertexColor.rgb), aVertexColor.a);

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    g_nVisibleLPV = uLightProbeIndex;
#endif

    gl_Position = g_matWorldToProjection * vec4(aVertexPosition, 1.0);
}
