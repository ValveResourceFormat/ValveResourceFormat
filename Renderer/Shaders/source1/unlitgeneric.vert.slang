#version 460

#include "unlitgeneric_features.slang"
#include "common_vs.slang"

in vec3 vNORMAL;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, vNORMAL);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);

    vVertexColorOut = GetSource1VertexColor();
}
