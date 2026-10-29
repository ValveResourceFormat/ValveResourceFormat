#version 460

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "common/surface_base_ps.slang"
#include "common/surface_material.slang"
#include "common/surface_shading.slang"
#include "common/surface_generic_material.slang"

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetGenericMaterial(texCoord, vertexNormals, texture(g_tColorA, texCoord), texture(g_tNormalA, texCoord), 0.0, 1.0, false, 1.0, false);
}

void main()
{
    RenderSurface(g_tColorA, DefaultSurfaceOptions());
}
