#version 460

in vec4 vtxColor;
in vec2 vtxUvs;
in vec3 vtxPos;

out vec4 outputColor;

#include "common/utils.slang"

void main(void)
{
    float yaw = cos(vtxColor.g * PI * 2.0);
    float pitch = sin(vtxColor.r * PI * 2.0);
    outputColor = vec4(yaw, pitch, vtxColor.a, 1.0);
}
