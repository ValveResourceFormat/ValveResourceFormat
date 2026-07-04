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
#elif (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    // Cables carry no lightmap coordinates; these combos exist only so the shared lighting include links.
    centroid out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

#if (S_SCENE_CUBEMAP_TYPE > 0)
    flat out uvec4 g_nEnvMapVisibility;
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
#elif (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec3(0.0);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vPerVertexLightingOut = vec3(0.0);
#endif

#if (S_SCENE_CUBEMAP_TYPE > 0)
    g_nEnvMapVisibility = uvec4(0u);
#endif

    gl_Position = g_matWorldToProjection * vec4(aVertexPosition, 1.0);
}
