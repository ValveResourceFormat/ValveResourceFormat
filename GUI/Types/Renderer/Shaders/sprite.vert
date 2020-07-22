#version 330 core

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;

void main()
{
    vTexCoordOut = vTEXCOORD;
    gl_Position = uProjectionViewMatrix * transform * vec4(vPOSITION, 1.0);
}
