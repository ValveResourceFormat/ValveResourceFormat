#version 460

#define renderMode_QuadOverdraw 0

// Count with the depth test the scene actually rendered with, and without the UAV writes disabling early z.
layout(early_fragment_tests) in;

// Image units 1 and 2 are taken by the MSAA resolve and depth pyramid compute passes
layout(binding = 3, r32ui) uniform uimage2D g_tQuadLock;
layout(binding = 4, r32ui) uniform uimage2D g_tQuadOverdraw;

layout(location = 0) out vec4 outputColor;

// False during the depth prime pass, which fills the depth buffer so that the
// counting pass only shades (and counts) the surfaces that end up visible.
uniform bool bCountQuads = true;

const uint QUAD_UNLOCKED = 0xFFFFFFFFu;

void main()
{
    outputColor = vec4(0.0, 0.0, 0.0, 1.0);

    if (!bCountQuads)
    {
        return;
    }

    ivec2 quad = ivec2(gl_FragCoord.xy * 0.5);
    uint primitiveId = uint(gl_PrimitiveID);

    // A primitive that touches a 2x2 quad shades it once per covered pixel, but must count only once.

    // The quad lock image serves as a scoreboard: each fragment tries to claim the quad with its primitive id, and only a claim
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
            lockValue = imageAtomicCompSwap(g_tQuadLock, quad, QUAD_UNLOCKED, primitiveId);
        }

        if (lockValue == QUAD_UNLOCKED)
        {
            // First pass claims the quad, the iteration after releases it.
            claimed++;

            if (claimed == 2)
            {
                lockValue = imageAtomicExchange(g_tQuadLock, quad, QUAD_UNLOCKED);
            }

            done = true;
        }

        done = done || lockValue == primitiveId;
    }

    if (claimed != 0)
    {
        imageAtomicAdd(g_tQuadOverdraw, quad, 1u);
    }
}
