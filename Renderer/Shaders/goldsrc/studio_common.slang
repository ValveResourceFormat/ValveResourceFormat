#version 460

#ifndef VRF_GOLDSRC_STUDIO_COMMON_INCLUDE
#define VRF_GOLDSRC_STUDIO_COMMON_INCLUDE

#include "studio_features.slang"
#include "../common/utils.slang"
#include "gamma.slang"

// Light that comes from one direction, travelling along g_vLightDirection, on top of ambient light. Both are
// brightnesses from 0 to 1 in the space of the light gamma curve, and g_vLightColor tints them. A model that the
// light probes of a map do not cover is lit by these, which are the values of a model that is shown alone.
uniform float g_flAmbientLight = 0.3;
uniform float g_flShadeLight = 0.6;
uniform vec3 g_vLightColor = vec3(1.0);
uniform vec3 g_vLightDirection = vec3(0.0, 0.0, -1.0);

// How far the light wraps around a sphere, where 1 is a plain dot product
uniform float g_flLambert = 1.4953241;

// Chrome textures are 64 texels wide in the space the reflection is looked up in
const float ChromeTexelScale = 32.0;

// A flat shaded surface takes this part of the shade light, whatever way it faces
const float FlatShadeFraction = 0.8;

// The light of a surface facing the normal, in the gamma space of the screen
vec3 GetStudioLight(vec3 normal)
{
    float shade = g_flShadeLight;

    if (F_FLAT_SHADE == 0)
    {
        float lightCos = min(dot(normal, g_vLightDirection), 1.0);
        float wrapped = (g_flLambert - 1.0 + lightCos) / g_flLambert;
        shade *= 1.0 - max(wrapped, 0.0);
    }
    else
    {
        shade *= FlatShadeFraction;
    }

    return GoldSrcLightGamma(vec3(g_flAmbientLight + shade)) * g_vLightColor;
}

#endif
