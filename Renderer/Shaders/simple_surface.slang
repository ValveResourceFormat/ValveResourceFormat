#version 460

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "common/surface_base_ps.slang"
#include "common/surface_material.slang"
#include "common/surface_shading.slang"
#include "common/surface_generic_material.slang"

uniform sampler2D g_tColorMetalness; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalRoughness; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColorMetalness, texCoord);
    vec4 normalTexture = texture(g_tNormalRoughness, texCoord);
    float ambientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;
    float metalness = color.a;

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, metalness, ambientOcclusion, false, 1.0, false);
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColorMetalness, options);
}
