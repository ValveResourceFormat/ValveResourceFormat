#version 460

#define F_ALPHA_TEST 0 // most shaders

#include "common/texturing.slang"

in vec2 vTexCoordOut;
in vec4 vTintOut;
in vec3 vFragPosition;

uniform sampler2D g_tColor; // SrgbRead(true)

layout(location = 0) out vec4 outputColor;

#include "common/oit.slang"

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    color.a = AlphaTestAntiAliasing(color.a, vTexCoordOut);
#endif

    outputColor = color * vTintOut;

#if (D_OIT == 1)
    outputColor = OitFinish(outputColor, vFragPosition);
#endif
}
