#version 460

#include "common/features.slang"

#define S_SCENE_CUBEMAP_TYPE 0 // 0 = None, 1 = Per-batch cube map, 2 = Per-scene cube map array

// The shared lighting code reads these per vertex. A full screen pass has nothing to hand them.
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    // Only lights pixels no probe volume contains, the fragment stage finds the rest itself
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

void main()
{
    vec2 uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);

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
        g_nEnvMapVisibility = uvec4(0u);
    #endif
}
