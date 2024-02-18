#version 460

#define F_ALPHA_TEST 0 // most shaders

#include "common/texturing.slang"

in vec2 vTexCoordOut;

uniform sampler2D g_tColor; // SrgbRead(true)

layout (location = 0) out vec4 outputColor;
#include "common/translucent.slang"

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    color.a = AlphaTestAntiAliasing(color.a, vTexCoordOut);
#endif

    outputColor = color;
    outputColor = WeightColorTranslucency(outputColor);
}
