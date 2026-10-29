#version 460

#include "fallback_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "common/surface_simple_vs.slang"
