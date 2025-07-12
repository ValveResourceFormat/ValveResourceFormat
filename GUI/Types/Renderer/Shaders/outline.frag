#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

out vec4 outputColor;

uniform sampler2D g_tSceneDepth;

void main()
{
    float objectDepth = gl_FragCoord.z;

    ivec2 vScreenPosition = ivec2(gl_FragCoord.xy);
    float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x + 0.0001;

    float diff = objectDepth - flSceneDepth;

    if (diff > 0.0)
    {
        outputColor = vec4(2.0, 2.0, 0.0, 0.1); // Visible outline color
    }
    else
    {
        outputColor = vec4(1.0, 1.0, 0.0, 0.05); // Obscured outline color
    }
}
