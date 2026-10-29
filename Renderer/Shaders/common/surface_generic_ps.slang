#version 460

#ifndef VRF_SURFACE_GENERIC_PS_INCLUDE
#define VRF_SURFACE_GENERIC_PS_INCLUDE

// Fragment shader of a lit surface with a color texture and a normal map, drawn with every combo its material sets.
// It serves the shaders that have no file of their own, and the shaders that only name their two textures differently.
// The shader includes its features file (see fallback_features.slang), then this, declares its color and normal
// samplers, writes GetMaterial() from GetGenericMaterial(), and main() as RenderGenericSurface() of its color sampler.

#define UNLIT ((F_FULLBRIGHT == 1) || (F_UNLIT == 1))

#define IBL 1
#define SUBSURFACE (F_SUBSURFACE_SCATTERING == 1)
#define DIFFUSE_WRAP (F_DIFFUSE_WRAP == 1)
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "surface_base_ps.slang"
#include "surface_material.slang"
#include "surface_shading.slang"
#include "surface_generic_material.slang"

void RenderGenericSurface(sampler2D colorTexture)
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendMode = F_BLEND_MODE;

#if (UNLIT)
    RenderUnlitSurface(colorTexture, options);
#else
    RenderSurface(colorTexture, options);
#endif
}

#endif
