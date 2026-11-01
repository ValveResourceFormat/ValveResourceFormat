#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec3 vFragPosition;

out vec2 vTexCoordOut;

#include "../common/view_constants.slang"
#include "../common/instancing.slang"

void main()
{
    vec3 fragPosition = vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix();
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    vTexCoordOut = vTEXCOORD;
}
