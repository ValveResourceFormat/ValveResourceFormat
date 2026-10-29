#version 460

#ifndef VRF_SHARED_COMBOS_INCLUDE
#define VRF_SHARED_COMBOS_INCLUDE

// Baked lighting
#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_PROBE 0
#define D_LPV_BLENDING 0

#define S_LIGHTMAP_VERSION_MINOR 0

#include "lighting_profile.slang"

uniform int F_RENDER_BACKFACES; // most shaders
uniform int F_DONT_FLIP_BACKFACE_NORMALS; // most shaders
uniform int F_RETRO_REFLECTIVE; // csgo_character, vr_complex

#endif
