#version 460

#include "unlitgeneric_features.slang"
#include "common_vs.slang"

out vec3 vFragPosition;
out vec2 vTexCoordOut;

void main()
{
    vFragPosition = vec4(vPOSITION, 1.0) * CalculateSkinTransform();
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
}
