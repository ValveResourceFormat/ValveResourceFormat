#version 400

in vec3 aVertexPosition;

uniform mat4 uProjectionMatrix;
uniform mat4 uModelViewMatrix;

uniform mat4 uModelMatrix;

out vec2 uv;

void main(void) {
    uv = aVertexPosition.xy * 0.5 + 0.5;
    gl_Position = uProjectionMatrix * uModelViewMatrix * uModelMatrix * vec4(aVertexPosition, 1.0);
}
