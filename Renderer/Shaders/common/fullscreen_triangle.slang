#version 460

#ifndef VRF_FULLSCREEN_TRIANGLE_INCLUDE
#define VRF_FULLSCREEN_TRIANGLE_INCLUDE

// A single triangle that covers the whole screen, drawn with three vertices and no vertex buffer.
vec4 GetFullscreenTrianglePosition()
{
    vec2 uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    return vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}

#endif
