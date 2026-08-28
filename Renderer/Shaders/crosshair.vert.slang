#version 460

in vec3 vPOSITION;
in vec4 vCOLOR;

out vec4 vtxColor;

uniform mat4 transform;

void main(void) {
    gl_Position = transform * vec4(vPOSITION, 1.0);
    vtxColor = vCOLOR;
}
