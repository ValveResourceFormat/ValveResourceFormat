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

void CountQuadOverdraw()
{
    const uint QUAD_UNLOCKED = 0u;

    ivec2 quad = ivec2(gl_FragCoord.xy * 0.5);
    int lockIndex = QuadOverdrawLockIndex(quad);

    // Zero means unlocked (so the buffer clears to all zeroes), quads are claimed with the primitive id offset by one.
    uint claimValue = uint(gl_PrimitiveID) + 1u;

    // A primitive that touches a 2x2 quad shades it once per covered pixel, but must count only once.

    // The quad lock serves as a scoreboard: each fragment tries to claim the quad with its primitive id, and only a claim
    // that observed the quad unlocked counts.

    // Fragments that see their primitive id lost the race to a sibling fragment and bail.

    // Fragments that see a different primitive spin while that primitive releases the
    // lock, bounded to keep the loop finite under heavy load.
    uint lockValue = 0u;
    bool done = false;
    int claimed = 0;

    for (int i = 0; i < 16; i++)
    {
        if (!done)
        {
            lockValue = atomicCompSwap(g_quadOverdraw[lockIndex], QUAD_UNLOCKED, claimValue);
        }

        if (lockValue == QUAD_UNLOCKED)
        {
            // First pass claims the quad, the iteration after releases it.
            claimed++;

            if (claimed == 2)
            {
                lockValue = atomicExchange(g_quadOverdraw[lockIndex], QUAD_UNLOCKED);
            }

            done = true;
        }

        done = done || lockValue == claimValue;
    }

    if (claimed != 0)
    {
        atomicAdd(g_quadOverdraw[lockIndex + 1], 1u);
    }
}

#endif
