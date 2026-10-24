#version 460

#pragma IgnoreMaterialData

#define F_ALPHA_TEST 0 // most shaders

#if (F_ALPHA_TEST == 1)
    layout (location = 0) in vec2 texCoord;
    uniform sampler2D g_tColor;
    uniform float g_flAlphaTestReference = 0.5;
#endif

// Writes where the selected objects are into the outline mask. Drawn once into red wherever they are, and once
// into green against the scene's depth, where it does not hide them. outline_post.frag then finds their edges.
layout (location = 0) out vec2 outOutlineMask;

void main()
{
    // What is cut out of an object is not part of its outline
    #if (F_ALPHA_TEST == 1)
        if (texture(g_tColor, texCoord).a - 0.001 < g_flAlphaTestReference)
        {
            discard;
        }
    #endif

    outOutlineMask = vec2(1.0);
}
