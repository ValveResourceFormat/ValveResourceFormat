#version 460

#define F_ALPHA_TEST 0 // most shaders

#if (F_ALPHA_TEST == 0)
    layout (early_fragment_tests) in;
#endif

#if (F_ALPHA_TEST == 1)
    layout (location = 0) in vec2 texCoord;
    layout (location = 0) out vec4 outputColor;
    uniform sampler2D g_tColor;
    uniform float g_flAlphaTestReference = 0.5;
#endif

void main()
{
    #if (F_ALPHA_TEST == 1)
        float opacity = texture(g_tColor, texCoord).a;
        if (opacity - 0.001 < g_flAlphaTestReference)
            discard;

        outputColor = vec4(0.0, 0.0, 0.0, 1.0);
    #endif
}
