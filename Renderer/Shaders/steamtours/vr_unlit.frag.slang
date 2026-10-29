#version 460

#include "vr_unlit_features.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;

out vec4 outputColor;

#if ((F_TRANSLUCENT == 1) || (F_ALPHA_TEST == 1))
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
#endif

#if (F_TRANSLUCENT == 1)
    uniform float g_flOpacityScale = 1.0;
#endif

uniform float g_flAlphaTestReference = 0.5;

#include "../common/texturing.slang"
#include "../common/fog.slang"

void main()
{
#if ((F_TRANSLUCENT == 1) || (F_ALPHA_TEST == 1))
    vec4 color = texture(g_tColor, vTexCoordOut);
    float opacity = color.a;
#else
    vec4 color = texture(g_tColor2, vTexCoordOut);
    float opacity = 1.0;
#endif

#if (F_TRANSLUCENT == 1)
    opacity *= g_flOpacityScale;
#endif

#if (F_ALPHA_TEST == 1)
    opacity = AlphaTestAntiAliasing(opacity, vTexCoordOut, g_flAlphaTestReference);
#endif

    outputColor = vec4(color.rgb * vVertexColorOut.rgb, opacity);

    ApplyFog(outputColor.rgb, vFragPosition);
}
