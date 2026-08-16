#version 460

#ifndef VRF_QUAD_OVERDRAW_INCLUDE
#define VRF_QUAD_OVERDRAW_INCLUDE

#include "ViewConstants.slang"

layout(std430, binding = 13) buffer QuadOverdrawBuffer
{
    // Two uints per 2x2 pixel quad: a scoreboard lock and the primitive count.
    uint g_quadOverdraw[];
};

// Derived from the view constants so skinning variants of the counting shader need no per-program state
int QuadOverdrawLockIndex(ivec2 quad)
{
    int nQuadsPerRow = (int(g_vViewportSize.x) + 1) / 2;
    return 2 * (quad.y * nQuadsPerRow + quad.x);
}

int QuadOverdrawCountIndex(ivec2 quad)
{
    return QuadOverdrawLockIndex(quad) + 1;
}

#endif
