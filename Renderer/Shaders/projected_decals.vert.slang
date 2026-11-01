#version 460

#include "common/fullscreen_triangle.slang"
#include "common/shared_combos.slang"

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    out vec4 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

void main()
{
    gl_Position = GetFullscreenTrianglePosition();

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        vLightmapUVScaled = vec4(0.0);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        vPerVertexLightingOut = vec3(0.0);
    #endif
}
