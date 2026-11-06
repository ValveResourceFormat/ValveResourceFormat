#version 460

#ifndef VRF_SOURCE1_RENDER_MODES_SURFACE_PS_INCLUDE
#define VRF_SOURCE1_RENDER_MODES_SURFACE_PS_INCLUDE

#include "common_rendermodes_ps.slang"

#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0

// Replaces the color with the normal view of the active render mode. The bump map is the tangent space normal of the
// material, the bump normals the world space normal that is lit.
void ApplySource1NormalRenderModes(inout vec4 color, vec3 geometricNormal, vec3 tangent, vec3 bumpMap, vec3 bumpNormal)
{
    if (g_iRenderMode == renderMode_Normals)
    {
        color = vec4(PackToColor(geometricNormal), 1.0);
    }
    else if (g_iRenderMode == renderMode_Tangents)
    {
        color = vec4(PackToColor(tangent), 1.0);
    }
    else if (g_iRenderMode == renderMode_BumpMap)
    {
        color = vec4(PackToColor(bumpMap), 1.0);
    }
    else if (g_iRenderMode == renderMode_BumpNormals)
    {
        color = vec4(PackToColor(bumpNormal), 1.0);
    }
}

#endif
