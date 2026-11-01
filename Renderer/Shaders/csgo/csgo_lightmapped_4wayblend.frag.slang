#version 460

#include "csgo_lightmapped_4wayblend_features.slang"

#define IBL (F_SPECULAR_INDIRECT == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, 0.0, 1.0, false, 1.0, false);
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR_DIRECT == 1;
    RenderSurface(g_tColor, options);
}
