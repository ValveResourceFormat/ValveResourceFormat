#version 460

in vec3 aVertexPosition;
in vec4 aVertexColor;
out vec4 vtxColor;

uniform mat4 uProjectionViewMatrix;

void main(void) {
    vtxColor = aVertexColor;
    gl_Position = uProjectionViewMatrix * vec4(aVertexPosition, 1.0);
}
