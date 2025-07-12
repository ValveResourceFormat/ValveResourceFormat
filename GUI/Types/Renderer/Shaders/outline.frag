#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

out vec4 outputColor;

uniform sampler2D g_tSceneDepth;

#define D_OUTLINE_PASS 0

void main()
{
    float objectDepth = gl_FragCoord.z;

    ivec2 vScreenPosition = ivec2(gl_FragCoord.xy);
    float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x + 0.0001;

    float diff = objectDepth - flSceneDepth;

    #if (D_OUTLINE_PASS == 0)
        outputColor = vec4(1.0, 0.0, 0.0, 1.0); // Object color
        return;
    #endif


    if (diff > 0.0)
    {
        outputColor = vec4(2.0, 2.0, flSceneDepth, 0.5); // Visible outline color
    }
    else
    {
        discard;
        outputColor = vec4(0.0, 1.0, 0.0, 0.2); // Obscured outline color
    }
}

