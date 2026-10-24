#version 460

#define F_ALPHA_TEST 0 // most shaders

// Edged in a solid color along the sides of the sprite
#define D_BORDER 0

#include "common/texturing.slang"

in vec2 vTexCoordOut;
in vec4 vTintOut;

uniform sampler2D g_tColor; // SrgbRead(true)

uniform vec4 g_vBorderColor = vec4(0.0, 0.0, 0.0, 1.0);
uniform float g_flBorderWidthPx = 1.0;

out vec4 outputColor;

// Editor sprites are unlit: the texture in the tint, cut out where it is faint
void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut) * vTintOut;

#if (D_BORDER == 1)
    // How many pixels the sides are away, from how far the texture moves across a pixel
    vec2 pixelsToSides = min(vTexCoordOut, 1.0 - vTexCoordOut) / fwidth(vTexCoordOut);

    if (min(pixelsToSides.x, pixelsToSides.y) < g_flBorderWidthPx)
    {
        color = g_vBorderColor;
    }
#endif

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    color.a = AlphaTestAntiAliasing(color.a, vTexCoordOut);
#else
    color.a = 1.0;
#endif

    // Without post processing, as in every debug render mode, the color is shown as it is written
    if (g_iRenderMode != 0)
    {
        color.rgb = SrgbLinearToGamma(color.rgb);
    }

    outputColor = color;
}
