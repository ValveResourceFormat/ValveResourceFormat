#version 460

#include "common/fog.slang"

in vec3 vtxPosWs;

out vec4 outputColor;

// Linear radiance of the light geometry, blended additively
uniform vec3 g_vLuminance;

void main(void)
{
    // Additive, so fog fades it out instead of tinting it
    outputColor = vec4(g_vLuminance * GetFogAttenuation(vtxPosWs), 1.0);
}
