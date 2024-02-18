#version 460

in vec4 vtxColor;
layout (location = 0) out vec4 outputColor;

void main(void) {
    outputColor = vtxColor;
}
