#version 460

#include "csgo_simple_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;
#if (F_METALNESS_TEXTURE == 0)
    uniform float g_flMetalness = 0.0;
#endif

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);
    float ambientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;
#if (F_METALNESS_TEXTURE == 0)
    float metalness = g_flMetalness;
#else
    float metalness = color.a;
#endif

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, metalness, ambientOcclusion, false, 1.0, false);
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColor, options);
}
