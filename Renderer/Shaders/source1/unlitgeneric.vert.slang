#version 460

#include "unlitgeneric_features.slang"
#include "common_vs.slang"

in vec3 vNORMAL;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;

#if (SOURCE1_TWO_TEXTURE == 1)
// $texture2transform
uniform vec4 g_vTexCoord2TransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vTexCoord2TransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);

out vec2 vTexCoord2Out;
#endif

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, vNORMAL);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);

#if (SOURCE1_TWO_TEXTURE == 1)
    vTexCoord2Out = TransformSource1TexCoord(g_vTexCoord2TransformRow0, g_vTexCoord2TransformRow1, vTEXCOORD);
#endif

    vVertexColorOut = GetSource1VertexColor();
}
