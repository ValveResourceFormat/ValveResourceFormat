#version 460

#include "csgo_static_overlay_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "surface_painted_vs.slang"
