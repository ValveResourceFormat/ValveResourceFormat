#version 460

in vec3 aVertexPosition;
out vec3 vtxPosition;

uniform mat4 uProjectionViewMatrix;

void main(void) {
    vtxPosition = aVertexPosition;
    gl_Position = uProjectionViewMatrix * vec4(aVertexPosition, 1.0);
}
