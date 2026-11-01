#version 460

#include "fallback_features.slang"
#include "common/surface_generic_ps.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetGenericMaterial(texCoord, vertexNormals, texture(g_tColor, texCoord), texture(g_tNormal, texCoord), 0.0, 1.0, false, 1.0, UNLIT);
}

void main()
{
    RenderGenericSurface(g_tColor);
}
