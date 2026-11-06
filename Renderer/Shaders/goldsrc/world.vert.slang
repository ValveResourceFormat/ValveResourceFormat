#version 460

#include "common_vs.slang"

// Lightmap coordinates in the atlas
in vec2 vLightmapUV;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec2 vLightmapUVOut;

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = vTEXCOORD;
    vLightmapUVOut = vLightmapUV;
}
