#version 460

// The entry file includes its game's features file first and defines GetMaterial() from GetVrSimpleMaterial(), with
// the bump strength its game supports.

#define UNLIT (F_UNLIT == 1)

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

#if (F_METALNESS_TEXTURE == 0)
    uniform float g_flMetalness = 0.0;
#else
    uniform sampler2D g_tAmbientOcclusion;
#endif

uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;

MaterialProperties_t GetVrSimpleMaterial(vec2 texCoord, vec3 vertexNormals, float bumpStrength)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

#if (F_METALNESS_TEXTURE == 0)
    float metalness = g_flMetalness;
    float ambientOcclusion = color.a;
#else
    float metalness = color.a;
    float ambientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).r;
#endif

#if (UNLIT)
    bool unlit = true;
#else
    bool unlit = false;
#endif

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, metalness, ambientOcclusion, false, bumpStrength, unlit);
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;

#if (UNLIT)
    RenderUnlitSurface(g_tColor, options);
#else
    RenderSurface(g_tColor, options);
#endif
}
