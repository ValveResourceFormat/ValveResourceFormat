#version 460

#ifndef VRF_LIGHTING_PROFILE_INCLUDE
#define VRF_LIGHTING_PROFILE_INCLUDE

// Lightmap Version Minor 0: SteamVR Home and scenes with no lightmaps; 1: hlvr; 2: cs2, steampal; 3-5: citadel.

// Shaders of the vr family define this first. At version 2 they keep the shading of version 1, and only
// take the environment map blending and the baked shadow mask of version 2, as Aperture Desk Job does.
#ifndef LIGHTMAP_VR_SHADING
#define LIGHTMAP_VR_SHADING 0
#endif

#define VR_SHADING ((S_LIGHTMAP_VERSION_MINOR == 1) || ((S_LIGHTMAP_VERSION_MINOR == 2) && (LIGHTMAP_VR_SHADING != 0)))

// Baked data
#define BAKED_LIGHTING_RGBM (S_LIGHTMAP_VERSION_MINOR == 0) // Environment maps and light probes
#define BAKED_LIGHT_INDICES (S_LIGHTMAP_VERSION_MINOR == 1) // Up to four baked lights per texel, by index and strength
#define BAKED_LIGHT_SHADOW_MASK (S_LIGHTMAP_VERSION_MINOR >= 2) // The baked shadows of up to four lights per texel
#define LIGHTMAP_SH (S_LIGHTMAP_VERSION_MINOR >= 3) // Spherical harmonics rather than one dominant direction
#define LIGHTMAP_SH_LUMINANCE (S_LIGHTMAP_VERSION_MINOR == 5) // One set of directions for every channel

// Directional lightmaps
#define LIGHTMAP_DIRECTION_CLAMP (S_LIGHTMAP_VERSION_MINOR >= 2) // Instead of the per material minimum z
#define LIGHTMAP_FLATNESS_BOOST (LIGHTMAP_DIRECTION_CLAMP && !VR_SHADING)
#define LIGHTMAP_SPECULAR_OCCLUSION ((S_LIGHTMAP_VERSION_MINOR < 2) || VR_SHADING)

// Baked shadows are filtered bicubically, in the shadow mask of lightmaps wider than this
#define LIGHTMAP_BICUBIC_SHADOWS (BAKED_LIGHT_SHADOW_MASK && !VR_SHADING)
#if (S_LIGHTMAP_VERSION_MINOR == 2)
    #define LIGHTMAP_BICUBIC_SHADOWS_MIN_WIDTH 4096.0
#else
    #define LIGHTMAP_BICUBIC_SHADOWS_MIN_WIDTH 0.0
#endif

// Environment maps
#define ENVMAP_PER_OBJECT (S_LIGHTMAP_VERSION_MINOR == 1) // One map per object, the plain reflection, no blending
#define ENVMAP_EDGE_FADE (S_LIGHTMAP_VERSION_MINOR >= 2) // Rather than a hard edge
#define ENVMAP_SPHERES (S_LIGHTMAP_VERSION_MINOR >= 3) // Spheres fade radially and parallax correct in world space
#define ENVMAP_PARALLAX_ROUGHNESS_MIX (S_LIGHTMAP_VERSION_MINOR >= 2) // Giving way to the plain reflection
#define ENVMAP_PARALLAX_NORMALIZED ((S_LIGHTMAP_VERSION_MINOR >= 3) || VR_SHADING)
#define ENVMAP_OBJECT_VISIBILITY ((S_LIGHTMAP_VERSION_MINOR < 2) || ((S_LIGHTMAP_VERSION_MINOR == 2) && (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0) && (D_BAKED_LIGHTING_FROM_PROBE == 0)))
#define ENVMAP_COVERAGE_NORMALIZED (S_LIGHTMAP_VERSION_MINOR == 2) // The maps found share all of the weight
#define SKY_FILLS_UNCOVERED (S_LIGHTMAP_VERSION_MINOR >= 3) // In both environment maps and light probes
#define ENVMAP_LOD_SKIPS_SMALLEST_MIP ((S_LIGHTMAP_VERSION_MINOR == 2) && !VR_SHADING) // Full roughness stops at the second smallest mip

// Ceiling on cubemap normalization brightening: 1 up to the starting roughness, rising to the maximum at 1
#if (VR_SHADING)
    #define CUBEMAP_NORMALIZATION_MAX 32.0
    #define CUBEMAP_NORMALIZATION_ROUGHNESS_BEGIN 0.1
#elif (S_LIGHTMAP_VERSION_MINOR >= 2)
    // The games ship with cubemap brightening disabled
    #define CUBEMAP_NORMALIZATION_MAX 1.0
    #define CUBEMAP_NORMALIZATION_ROUGHNESS_BEGIN 0.0
#else
    #define CUBEMAP_NORMALIZATION_MAX 3.0
    #define CUBEMAP_NORMALIZATION_ROUGHNESS_BEGIN 0.0
#endif

// Light probe volumes
#define PROBE_BLENDING ((S_LIGHTMAP_VERSION_MINOR >= 2) && !VR_SHADING) // Else the one volume picked for the object
#define PROBES_FROM_ENVMAPS (S_LIGHTMAP_VERSION_MINOR >= 3) // Through the environment map sharing their handshake

// BRDF
#define MULTISCATTER (!VR_SHADING) // Also takes the energy it reflects off the diffuse
#define MULTISCATTER_REFLECTS_ENVIRONMENT ((S_LIGHTMAP_VERSION_MINOR == 2) && !VR_SHADING) // Else the irradiance
#define DIFFUSE_ENERGY_CLAMPED (S_LIGHTMAP_VERSION_MINOR < 2)
#define SPECULAR_ENERGY_COMPENSATION (!VR_SHADING)
#define SPECULAR_HEIGHT_CORRELATED_VISIBILITY (S_LIGHTMAP_VERSION_MINOR >= 3) // Else Schlick-Smith
#define GEOMETRIC_ROUGHNESS (S_LIGHTMAP_VERSION_MINOR < 3) // Roughness raised where the normal changes quickly
#define ROUGHNESS_FLOOR_OPAQUE (S_LIGHTMAP_VERSION_MINOR >= 3) // Opaque surfaces are lit with a minimum roughness; translucent and glass are not

// Fog
#define FOG_GRADIENT_HORIZONTAL_DISTANCE (VR_SHADING || (S_LIGHTMAP_VERSION_MINOR >= 3)) // Else the distance from the camera
// Fog takes the pixels past its start or above its culling height. Else the pixels past its start and on the side
// of the culling height its height range grows to.
#define FOG_CULLING_FIXED_HEIGHT_SIDE (S_LIGHTMAP_VERSION_MINOR == 1)

#endif
