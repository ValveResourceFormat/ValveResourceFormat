#version 460

uniform mat4 transform;

void main(void) {
    vec2 uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vec4 position = vec4(uv * vec2(2, -2) + vec2(-1, 1), 0, 1);

    gl_Position = transform * position;
}
