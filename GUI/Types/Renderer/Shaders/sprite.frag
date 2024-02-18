#version 460

//Parameter defines - These are default values and can be overwritten based on material parameters
#define F_ALPHA_TEST 0
//End of parameter defines

in vec2 vTexCoordOut;

uniform sampler2D g_tColor; // SrgbRead(true)
uniform float g_flAlphaTestReference;

layout (location = 0) out vec4 outputColor;

#include "common/translucent.glsl"

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

    outputColor = color;
    outputColor = WeightColorTranslucency(outputColor);
}
