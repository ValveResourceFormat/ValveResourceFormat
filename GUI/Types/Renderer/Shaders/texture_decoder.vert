#version 460

// fullscreen triangle vertices
const vec2 vertices[3] = vec2[3](vec2(-1.0f, -1.0f), vec2(3.0f, -1.0f), vec2(-1.0f, 3.0f));

void main()
{
    gl_Position = vec4(vertices[gl_VertexID], 1.0f);
}
