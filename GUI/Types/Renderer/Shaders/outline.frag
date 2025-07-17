#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

uniform sampler2D g_tSceneDepth;

void main()
{
#if defined(GL_ARB_shader_stencil_export)
    float objectDepth = gl_FragCoord.z;

    ivec2 vScreenPosition = ivec2(gl_FragCoord.xy);
    float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x + 0.0001;

    float diff = objectDepth - flSceneDepth;

    if (diff > 0.0)
    {
        gl_FragStencilRefARB = 0x02; // Visible outline
    }
    else
    {
        gl_FragStencilRefARB = 0x01; // Obscured outline
    }
#endif
}
