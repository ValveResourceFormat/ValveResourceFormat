#version 460

#ifndef VRF_SURFACE_FOLIAGE_INCLUDE
#define VRF_SURFACE_FOLIAGE_INCLUDE

// The foliage params that the vertex shader passes on, and the occlusion they paint into the material.
// Include it after surface_base_ps.slang.

in vec4 vFoliageParamsOut;

uniform float g_flVertexAmbientOcclusionPower = 1.0;
uniform float g_flVertexAmbientOcclusionAmount = 0.0;

// Darkens the ambient occlusion by the vertex occlusion in the alpha of the foliage params
void ApplyFoliageOcclusion(inout MaterialProperties_t mat)
{
    float vertexOcclusion = clamp(vFoliageParamsOut.w, 0.001, 1.0);
    mat.AmbientOcclusion *= mix(1.0, pow(vertexOcclusion, g_flVertexAmbientOcclusionPower), g_flVertexAmbientOcclusionAmount);
}

#endif
