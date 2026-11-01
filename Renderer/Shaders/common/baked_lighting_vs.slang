#version 460

#ifndef VRF_BAKED_LIGHTING_VS_INCLUDE
#define VRF_BAKED_LIGHTING_VS_INCLUDE

// The baked lighting inputs and outputs of the lighting mode the shader compiles for.
// The shader declares D_BAKED_LIGHTING_FROM_LIGHTMAP and D_BAKED_LIGHTING_FROM_VERTEX_STREAM first.

#include "utils.slang"
#include "lighting_constants.slang"

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    centroid out vec4 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting; // COLOR1
    out vec3 vPerVertexLightingOut;
#endif

// Writes the baked lighting outputs of the lighting mode the shader compiles for
void WriteBakedLighting()
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = GetLightmapCoordinates(vLightmapUV);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    vPerVertexLightingOut = pow2(Light);
#endif
}

#endif
