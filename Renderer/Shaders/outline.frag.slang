#version 460

#pragma IgnoreMaterialData

#include "common/ViewConstants.slang"

#define F_ALPHA_TEST 0 // most shaders

#if (F_ALPHA_TEST == 1)
    layout (location = 0) in vec2 texCoord;
    uniform sampler2D g_tColor;
    uniform float g_flAlphaTestReference = 0.5;
#endif

uniform sampler2D g_tSceneDepth; // reserved, resolved scene depth (reverse-Z window space)

// Writes where the selected objects are into the outline mask: red wherever they are, green where the scene
// does not hide them. outline_post.frag then finds their edges.
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

    // Reverse-Z, so what is in front has the larger depth
    float sceneDepth = texelFetch(g_tSceneDepth, ivec2(gl_FragCoord.xy), 0).x;
    float isVisible = gl_FragCoord.z >= sceneDepth - 1e-6 ? 1.0 : 0.0;

    outOutlineMask = vec2(1.0, isVisible);
}
