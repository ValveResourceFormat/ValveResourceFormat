#version 460

// The entry file includes the features file of its game first.

// The shader has no normal map, gloss or cube map, so a surface only takes diffuse light
#define UNLIT (F_UNLIT == 1)
#define IBL 0
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"

// The material names its color texture by whether it needs the alpha channel
#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
    #define COLOR_TEXTURE g_tColor
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
    #define COLOR_TEXTURE g_tColor2
#endif

#include "vr_surface_lighting_extras.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat = InitGenericMaterial(texCoord, vertexNormals, texture(COLOR_TEXTURE, texCoord), false);

#if (UNLIT)
    // Unlit, the glow replaces the color instead of adding to it
    #if (F_SELF_ILLUM == 1)
        mat.Albedo *= GetSelfIllumScale(texCoord);
    #endif

    FinishUnlitMaterial(mat);
    return mat;
#endif

    mat.NormalMap = vec3(0.0, 0.0, 1.0);
    mat.Normal = mat.GeometricNormal;
    mat.RoughnessTex.xy = vec2(1.0);
    mat.Metalness = 0.0;
    mat.AmbientOcclusion = 1.0;

    FinishLitMaterial(mat, texCoord, vec3(0.0));

    // The glow is added to the diffuse lighting, so it takes the diffuse color
    mat.IllumColor = GetSelfIllumScale(texCoord) * mat.DiffuseColor;

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = false;
    options.SpecularIndirectScale = 0.0;

#if (F_WORLD_LIGHTING_GRADIENT == 1)
    options.IndirectLightScale = GetWorldLightingGradient(vFragPosition);
#endif

#if (UNLIT)
    RenderUnlitSurface(COLOR_TEXTURE, options);
#else
    RenderSurface(COLOR_TEXTURE, options);
#endif
}
