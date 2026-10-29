#version 460

#ifndef VRF_SURFACE_MODEL_TINT_VS_INCLUDE
#define VRF_SURFACE_MODEL_TINT_VS_INCLUDE

// The model tint amount and fade exponent of the vertex color, for the surfaces whose shader has them.
// Include it after surface_base_vs.slang.

uniform float g_flModelTintAmount = 1.0;
uniform float g_flFadeExponent = 1.0;

void WriteSurfaceVertex(bool scrollsAnimatedAtlas)
{
    WriteSurfaceVertex(scrollsAnimatedAtlas, g_flModelTintAmount, g_flFadeExponent);
}

#endif
