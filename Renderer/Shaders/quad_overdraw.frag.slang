#version 460

#define renderMode_QuadOverdraw 0
#define F_ALPHA_TEST 0 // set per draw from the material, see MeshBatchRenderer

#include "common/quad_overdraw.slang"

#if (F_ALPHA_TEST == 0)
    // Count with the depth test the scene actually rendered with, and without the UAV writes disabling early z.
    // Alpha tested draws cannot have it: an early depth write would record the fragments they discard.
    layout(early_fragment_tests) in;
#else
    layout (location = 0) in vec2 texCoord;
    uniform sampler2D g_tColor;
    uniform float g_flAlphaTestReference = 0.5;
#endif

layout(location = 0) out vec4 outputColor;

// False during the depth prime pass, which fills the depth buffer so that the
// counting pass only shades (and counts) the surfaces that end up visible.
uniform bool bCountQuads = true;

const uint QUAD_UNLOCKED = 0u;

void main()
{
    // Before anything is counted, so cut out geometry costs what it really costs.
    #if (F_ALPHA_TEST == 1)
        float opacity = texture(g_tColor, texCoord).a;
        if (opacity - 0.001 < g_flAlphaTestReference)
            discard;
    #endif

    outputColor = vec4(0.0, 0.0, 0.0, 1.0);

    if (!bCountQuads)
    {
        return;
    }

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
