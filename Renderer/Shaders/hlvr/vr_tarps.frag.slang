#version 460

#include "vr_tarps_features.slang"

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
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
uniform float g_flMetalness = 0.0;

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);
    float ambientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;
    float metalness = g_flMetalness;

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, metalness, ambientOcclusion, true, 1.0, false);
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColor, options);
}
