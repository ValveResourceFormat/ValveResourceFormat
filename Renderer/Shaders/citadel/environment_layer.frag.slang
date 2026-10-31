#version 460

// Writes the bloom amount of the pixel for the bloom threshold
#define F_OVERRIDE_BLOOM_AMOUNT 0

// Rect lights shade as area lights, the way Deadlock does it
#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// Deadlock blends the light probe volumes in every lightmap version
#define ENABLE_LPV_BLENDING 1

#define SCREEN_SPACE_AO 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_environment_layers.slang"

in vec3 vColorBlendValues;

#include "environment_layer1_uniforms.slang"

uniform vec4 g_vScaleRotationTranslation1 = vec4(1.0, 0.0, 0.0, 0.0);

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    LayerMapping_t mapping = GetMeshMapping(ApplyUvTransform(texCoord, g_vScaleRotationTranslation1));

    Layer_t layer = SampleLayer(g_tColor1, g_tNormalRoughness1, g_tPacked1, mapping,
        g_mAlbedoColorCorrect1, g_mTextureColorTint1,
        g_vLayerRoughnessContrastBrightness1, g_vLayerTintMaskContrastBrightness1, g_vLayerMetalnessContrastBrightness1,
        g_fLayerNormalStrength1, g_fLayerSelfIllumScale1,
        g_bMaskColorTint1, g_bMaskVertexColorTint1, g_bModelTint1, g_fVertexColorStrength1);

    vec3 illumColor = layer.Albedo * layer.SelfIllum * g_vLayerSelfIllumTint1;

    ResolveLayerMaterial(mat, layer, illumColor);

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    // Stored as it is, without the scale or a mask
    mat.BloomAmount = saturate(g_flOverrideBloomAmount1);
#endif

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendValues = vec3(vColorBlendValues.xy, 0.0);
    RenderSurface(g_tColor1, options);
}
