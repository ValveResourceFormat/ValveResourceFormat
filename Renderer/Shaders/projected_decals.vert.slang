#version 460

#include "common/features.slang"

// The shared lighting code reads these per vertex. A full screen pass has nothing to hand them.
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

void main()
{
    vec2 uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        vLightmapUVScaled = vec3(0.0);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        vPerVertexLightingOut = vec3(0.0);
    #endif
}
