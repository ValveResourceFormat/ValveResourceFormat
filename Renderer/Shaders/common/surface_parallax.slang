#version 460

#ifndef VRF_SURFACE_PARALLAX_INCLUDE
#define VRF_SURFACE_PARALLAX_INCLUDE

// Layers that sit below the surface, offset along the view ray. Include it after surface_base_ps.slang.

// Each layer has its own UV transform, and sits its displacement below the surface along the view ray.
// The displacement is in 1024ths of a texture.
vec2 GetLayerTexCoord(vec2 texCoord, vec2 scale, vec2 offset, float displacement, vec2 parallaxDir)
{
    return texCoord * scale + offset + parallaxDir * (displacement / 1024.0);
}

vec2 GetParallaxDirection(MaterialProperties_t mat)
{
    vec3 viewDirInv = -mat.ViewDir;
    float invDepth = 1.0 / dot(viewDirInv, mat.GeometricNormal);
    vec2 viewDirTs = vec2(dot(viewDirInv, mat.Tangent), dot(viewDirInv, mat.Bitangent));
    return viewDirTs * invDepth;
}

#endif
