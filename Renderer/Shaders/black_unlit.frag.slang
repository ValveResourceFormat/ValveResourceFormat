#version 460

#include "common/utils.slang"

in vec3 vFragPosition;
out vec4 outputColor;

#include "common/view_constants.slang"

#include "common/fog.slang"

void main() {
    outputColor.rgb = vec3(0.0);

    if (g_bFogEnabled)
    {
        ApplyFog(outputColor.rgb, vFragPosition);
    }
}
