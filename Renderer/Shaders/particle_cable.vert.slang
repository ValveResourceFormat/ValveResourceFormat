#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec2 vTEXCOORD;
in vec4 vCOLOR;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    // Cables carry no lightmap coordinates; these combos exist only so the shared lighting include links.
    centroid out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    uniform uint uLightProbeIndex = 0u;
    flat out uint g_nVisibleLPV;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

#if (S_SCENE_CUBEMAP_TYPE == 1)
    flat out uint g_nEnvMapIndex;
#elif (S_SCENE_CUBEMAP_TYPE == 2)
    flat out uvec4 g_nEnvMapVisibility;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vColorOut;

void main(void)
{
    vFragPosition = vPOSITION;
    vNormalOut = vNORMAL;
    vTexCoordOut = vTEXCOORD;

    // Per-node colour (COLOR stream, whole-cable tint already folded in). The CPU packs the authored
    // gamma-space colour raw into the byte stream, so one decode here nets the final colour. The alpha
    // rides along but the fragment stage does not read it.
    vColorOut = vec4(SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec3(0.0);
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    g_nVisibleLPV = uLightProbeIndex;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vPerVertexLightingOut = vec3(0.0);
#endif

#if (S_SCENE_CUBEMAP_TYPE == 1)
    g_nEnvMapIndex = 0u;
#elif (S_SCENE_CUBEMAP_TYPE == 2)
    g_nEnvMapVisibility = uvec4(0u); // no envmap fetch?
#endif

    gl_Position = g_matWorldToProjection * vec4(vPOSITION, 1.0);
}
