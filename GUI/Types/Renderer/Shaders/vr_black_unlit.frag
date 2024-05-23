#version 460

#include "common/utils.glsl"

in vec3 vFragPosition;
out vec4 outputColor;

#include "common/ViewConstants.glsl"

#include "common/fog.glsl"

void main(void) {
    outputColor.rgb = vec3(0.0);

    if (g_bFogEnabled)
    {
        ApplyFog(outputColor.rgb, vFragPosition);
        outputColor.rgb = outputColor.rgb;
    }
}
