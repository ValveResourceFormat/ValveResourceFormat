#version 460

in vec3 aVertexPosition;

uniform mat4 uProjectionViewMatrix;
uniform mat4 uModelMatrix;

out vec2 uv;

void main(void) {
    uv = aVertexPosition.xy * 0.5 + 0.5;
    gl_Position = uProjectionViewMatrix * uModelMatrix * vec4(aVertexPosition, 1.0);
}
