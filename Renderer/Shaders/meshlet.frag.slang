#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"

#define renderMode_MeshShader 0
#define renderMode_Normals 0
#define renderMode_Meshlets 0

layout (location = 0) in vec3 vFragPosition;
layout (location = 1) in vec3 vNormalOut;
layout (location = 2) in vec2 vTexCoordOut;
layout (location = 3) flat in uint vMeshletIdOut;

out vec4 outputColor;

vec3 MeshletColor(uint nMeshlet)
{
    uint nHash = nMeshlet * 1664525u + 1013904223u;
    nHash ^= nHash >> 15u;

    return vec3(
        float((nHash >> 0u) & 0xFFu),
        float((nHash >> 8u) & 0xFFu),
        float((nHash >> 16u) & 0xFFu)) / 255.0;
}

void main()
{
    vec3 vNormal = normalize(vNormalOut);

    if (g_iRenderMode == renderMode_Normals)
    {
        outputColor = vec4(PackToColor(vNormal), 1.0);
        return;
    }

    vec3 vAlbedo = MeshletColor(vMeshletIdOut);

    if (g_iRenderMode == renderMode_Meshlets)
    {
        outputColor = vec4(vAlbedo, 1.0);
        return;
    }

    // Enough shaping to read the silhouette without pulling the scene's lighting into a debug pass
    vec3 vViewDir = normalize(g_vCameraPositionWs - vFragPosition);
    float flDiffuse = saturate(dot(vNormal, vViewDir)) * 0.7 + 0.3;

    outputColor = vec4(vAlbedo * flDiffuse, 1.0);
}
