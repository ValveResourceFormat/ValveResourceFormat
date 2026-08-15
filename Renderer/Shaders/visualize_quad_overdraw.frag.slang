#version 460

#include "common/quad_overdraw.slang"

layout(location = 0) out vec4 outputColor;

// matches the color table as found in CS2
vec4 OverdrawColor(uint count)
{
    switch (count)
    {
        case 0u: return vec4(0.0, 0.0, 0.0, 1.0);
        case 1u: return vec4(0.0, 0.0, 0.5, 1.0);
        case 2u: return vec4(0.0, 0.3, 1.0, 1.0);
        case 3u: return vec4(0.0, 0.9, 1.0, 1.0);
        case 4u: return vec4(0.3, 1.0, 0.0, 1.0);
        case 5u: return vec4(0.9, 1.0, 0.0, 1.0);
        case 6u: return vec4(1.0, 0.3, 0.0, 1.0);
        case 7u: return vec4(1.0, 0.0, 0.3, 1.0);
        case 8u: return vec4(1.0, 0.0, 0.9, 1.0);
        case 9u: return vec4(1.0, 0.3, 0.9, 1.0);
        default: return vec4(1.0, 1.0, 1.0, 1.0);
    }
}

void main()
{
    ivec2 quad = ivec2(gl_FragCoord.xy * 0.5);
    outputColor = OverdrawColor(g_quadOverdraw[QuadOverdrawCountIndex(quad)]);

    // heat map legend near the bottom of the screen, flipped on y compared to Source 2 because gl_FragCoord is y-up
    vec2 uv = gl_FragCoord.xy * g_vInvViewportSize;
    uv.y = 1.0 - uv.y;

    if (all(greaterThan(uv, vec2(0.1, 0.9))) && all(lessThan(uv, vec2(0.9, 0.92))))
    {
        outputColor = vec4(0.0, 0.0, 0.0, 1.0);

        if (all(greaterThan(uv, vec2(0.1015, 0.9015))) && all(lessThan(uv, vec2(0.8985, 0.9185))))
        {
            outputColor = OverdrawColor(uint((uv.x - 0.1) * 13.75));
        }
    }
}
