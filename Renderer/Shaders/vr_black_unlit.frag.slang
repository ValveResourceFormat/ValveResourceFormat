#version 460

#include "common/utils.slang"

in vec3 vFragPosition;
out vec4 outputColor;

#include "common/ViewConstants.slang"

#include "common/fog.slang"

void main(void) {
    outputColor.rgb = vec3(0.0);

    if (g_bFogEnabled)
    {
        ApplyFog(outputColor.rgb, vFragPosition);
        outputColor.rgb = outputColor.rgb;
    }
}
