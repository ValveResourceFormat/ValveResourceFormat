#version 460

#include "common/utils.glsl"

in vec3 vFragPosition;
out vec4 outputColor;

#include "common/ViewConstants.glsl"

#include "common/fog.glsl"

void main(void) {
    outputColor = vec4(0.0, 0.0, 0.0, 1.0);

    ApplyFog(outputColor.rgb, vFragPosition);
}
