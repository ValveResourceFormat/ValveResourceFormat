#version 460

#define renderMode_QuadOverdraw 0
#define F_ALPHA_TEST 0 // set per draw from the material, see MeshBatchRenderer

#include "common/quad_overdraw.slang"

layout(early_fragment_tests) in;

#if (F_ALPHA_TEST == 1)
    layout (location = 0) in vec2 texCoord;
    uniform sampler2D g_tColor;
    uniform float g_flAlphaTestReference = 0.5;
#endif

layout(location = 0) out vec4 outputColor;

void main()
{
    outputColor = vec4(0.0, 0.0, 0.0, 1.0);

    #if (F_ALPHA_TEST == 1)
        float opacity = texture(g_tColor, texCoord).a;
        if (opacity - 0.001 < g_flAlphaTestReference)
            discard;
    #endif

    CountQuadOverdraw();
}
