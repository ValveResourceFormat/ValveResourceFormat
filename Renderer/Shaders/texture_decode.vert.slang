#version 460

#include "common/fullscreen_triangle.slang"

void main()
{
    gl_Position = GetFullscreenTrianglePosition();
}
