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
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

flat out uint g_nObjectIndex;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vColorOut;

void main(void)
{
    vFragPosition = vPOSITION;
    vNormalOut = vNORMAL;
    vTexCoordOut = vTEXCOORD;

    // Per-node colour, whole-cable tint folded in. The CPU packs gamma-space colour raw into the byte stream,
    // so one decode here gives the final colour. Alpha is unused by the fragment stage.
    vColorOut = vec4(SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec3(0.0);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vPerVertexLightingOut = vec3(0.0);
#endif

    g_nObjectIndex = gl_BaseInstance;

    gl_Position = g_matWorldToProjection * vec4(vPOSITION, 1.0);
}
