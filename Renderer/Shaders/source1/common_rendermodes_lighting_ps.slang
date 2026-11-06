#version 460

#ifndef VRF_SOURCE1_RENDER_MODES_LIGHTING_PS_INCLUDE
#define VRF_SOURCE1_RENDER_MODES_LIGHTING_PS_INCLUDE

#include "common_rendermodes_surface_ps.slang"

#define renderMode_Diffuse 0
#define renderMode_Specular 0

// Replaces the color with the light that reaches the surface, before the albedo multiplies the diffuse light
void ApplySource1LightingRenderModes(inout vec4 color, vec3 diffuseLight, vec3 specularLight)
{
    if (g_iRenderMode == renderMode_Diffuse)
    {
        color = vec4(SrgbLinearToGamma(diffuseLight), 1.0);
    }
    else if (g_iRenderMode == renderMode_Specular)
    {
        color = vec4(SrgbLinearToGamma(specularLight), 1.0);
    }
}

#endif
