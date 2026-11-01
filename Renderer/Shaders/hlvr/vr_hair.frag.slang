#version 460

#include "vr_hair_features.slang"

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
#define IBL 1
#define SUBSURFACE 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_generic_material.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;

uniform float g_flMetalness = 0.0;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

#if (!TRANSLUCENT)
    // The opacity scale fades every hair, but InitOpacity() applies it only to translucent materials
    color.a *= g_flOpacityScale;
#endif

    float ambientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;

    return GetGenericMaterial(texCoord, vertexNormals, color, normalTexture, g_flMetalness, ambientOcclusion, true, 1.0, false);
}

#if (S_MODE_DEPTH == 0)
// The same surface seen from its other side, which the game lights with the flipped normal
MaterialProperties_t FlipMaterialSide(MaterialProperties_t mat)
{
    mat.GeometricNormal = -mat.GeometricNormal;
    mat.Normal = -mat.Normal;
    mat.SoftNormal = -mat.SoftNormal;
    mat.AmbientGeometricNormal = -mat.AmbientGeometricNormal;
    mat.AmbientNormal = -mat.AmbientNormal;
    return mat;
}
#endif

void main()
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
#else
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectDiffuseAO = 0.0;
    options.DirectSpecularAO = 1.0;

    MaterialProperties_t mat = GetShadedMaterial();
    MaterialProperties_t backMat = FlipMaterialSide(mat);

    LightingTerms_t lighting;
    LightingTerms_t backLighting;
    vec4 frontColor = ShadeSurface(mat, lighting, options);
    vec4 backColor = ShadeSurface(backMat, backLighting, options);

    // Hair is lit from both sides, and the result is the average of the two
    outputColor = vec4((frontColor.rgb + backColor.rgb) * 0.5, frontColor.a);

    FinishSurface(mat, lighting, options, g_tColor);
#endif
}
