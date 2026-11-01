#version 460

#define F_ALPHA_TEST 0 // most shaders

// Edged in a solid color along the sides of the sprite
#define D_BORDER 0

#include "../common/texturing.slang"

in vec2 vTexCoordOut;
in vec4 vTintOut;

// The alpha test mode picks the texture: color only, alpha from a separate texture, or alpha in the color texture
#if (F_ALPHA_TEST == 1)
    uniform sampler2D g_tColorB; // SrgbRead(true)
#elif (F_ALPHA_TEST == 2)
    uniform sampler2D g_tColorC; // SrgbRead(true)
#else
    uniform sampler2D g_tColorA; // SrgbRead(true) RepresentativeTexture
#endif

uniform vec4 g_vBorderColor = vec4(0.0, 0.0, 0.0, 1.0);
uniform float g_flBorderWidthPx = 1.0;
uniform float g_flAlphaTestReference = 0.5;

out vec4 outputColor;

// Editor sprites are unlit: the texture in the tint, cut out where it is faint
void main()
{
#if (F_ALPHA_TEST == 1)
    vec4 color = texture(g_tColorB, vTexCoordOut);
#elif (F_ALPHA_TEST == 2)
    vec4 color = texture(g_tColorC, vTexCoordOut);
#else
    vec4 color = texture(g_tColorA, vTexCoordOut);
#endif

    color *= vTintOut;

#if (D_BORDER == 1)
    // How many pixels the sides are away, from how far the texture moves across a pixel
    vec2 pixelsToSides = min(vTexCoordOut, 1.0 - vTexCoordOut) / fwidth(vTexCoordOut);

    if (min(pixelsToSides.x, pixelsToSides.y) < g_flBorderWidthPx)
    {
        color = g_vBorderColor;
    }
#endif

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    color.a = AlphaTestAntiAliasing(color.a, vTexCoordOut, g_flAlphaTestReference);
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
