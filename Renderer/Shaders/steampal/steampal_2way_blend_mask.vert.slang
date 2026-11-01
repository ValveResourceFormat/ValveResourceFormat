#version 460

// Blends by its mask alone, without vertex blend values
#include "../hlvr/vr_simple_2way_blend_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_simple_vs.slang"
