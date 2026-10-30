#version 460

#ifndef VRF_SHARED_COMBOS_INCLUDE
#define VRF_SHARED_COMBOS_INCLUDE

// Baked lighting
#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_PROBE 0
#define D_LPV_BLENDING 0

#define S_LIGHTMAP_VERSION_MINOR 0
#define S_FOG_CUBE_BLEND_MAX 1 // Cubemap fog takes the stronger of its distance and height terms
#define S_VR_FAST_PATH_LIGHTING 0 // Shades one shadowed light and diffuse-only point lights, and lights the rest with the sky only

#include "lighting_profile.slang"

uniform int F_RENDER_BACKFACES; // most shaders
uniform int F_DONT_FLIP_BACKFACE_NORMALS; // most shaders
uniform int F_RETRO_REFLECTIVE; // csgo_character, vr_complex

#endif
