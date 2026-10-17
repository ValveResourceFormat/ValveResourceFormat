#version 460

// Edged in a solid color along the sides of the quad
#define D_BORDER 0

in vec4 vColorOut;
in vec2 vTexCoord;
flat in vec4 vRectPx;

uniform sampler2D g_tColor;

// Below this alpha the texture is cut away, or nothing is when negative
uniform float g_flAlphaTestReference;

const vec4 BorderColor = vec4(0.0, 0.0, 0.0, 1.0);
const float BorderWidthPx = 1.0;

out vec4 outputColor;

void main(void)
{
    vec4 color = vColorOut * texture(g_tColor, vTexCoord);

#if D_BORDER == 1
    if (gl_FragCoord.x < vRectPx.x + BorderWidthPx || gl_FragCoord.x > vRectPx.z - BorderWidthPx
        || gl_FragCoord.y < vRectPx.y + BorderWidthPx || gl_FragCoord.y > vRectPx.w - BorderWidthPx)
    {
        color = BorderColor;
    }
#endif

    if (color.a < g_flAlphaTestReference)
    {
        discard;
    }

    outputColor = vec4(color.rgb, 1.0);
}
