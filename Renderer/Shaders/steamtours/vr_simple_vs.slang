#version 460

// The entry file includes the features file of its game first.

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_simple_vs.slang"
