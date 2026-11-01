#version 460

#include "csgo_environment_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
#define ALPHA_TEST_COVERAGE_BY_UV_FOOTPRINT 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_height_layers.slang"
#include "../common/wetness.slang"

in vec4 vTintColor_ModelAmount;
in vec4 vColorBlendValues;

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
#endif

#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference = 0.5;
#endif
uniform sampler2D g_tColor1; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    uniform sampler2D g_tNormalDetail1;
    uniform int g_nDetailUVSet1 = -1; // -1=Inherit, 0=Biplanar, 1=UV1, 2=UV2
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);
    uniform float g_fDetailTextureNormalContrast1 = 1.0; // -2 .. 2
    uniform float g_flDetailTexCoordRotation1 = 0.0;
#endif

uniform bool g_bMetalness1 = true;
uniform bool g_bModelTint1 = true;
uniform int g_nColorCorrectionMode1 = 0; // 1 = adjust the base color through g_mTextureAdjust1
uniform float g_fTextureNormalContrast1 = 1.0;
uniform float g_fTextureRoughnessBrightness1 = 1.0;
uniform float g_fTextureRoughnessContrast1 = 1.0;
uniform float g_fTintMaskBrightness1 = 1.0;
uniform float g_fTintMaskContrast1 = 1.0;
uniform vec3 g_vAmbientOcclusionLevels1 = vec3(0, 0.5, 1);

uniform mat4 g_mTextureColorAdjust1;
uniform mat4 g_mTextureAdjust1;

uniform int g_nUVSet1 = 1; // 0=Biplanar, 1=UV1, 2=UV2
uniform vec2 g_vTexCoordScale1 = vec2(1.0);
uniform float g_flTexCoordRotation1 = 0.0;

// The dielectric reflectance the specular color is built from. Wetness pulls it toward water.
uniform float g_flReflectance = 0.04;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    BiplanarFrame_t biplanar = GetBiplanarFrame(mat.GeometricNormal, vFragPosition);

    HeightLayer_t layer = SampleHeightLayer(g_tColor1, g_tHeight1, g_tNormal1, g_nUVSet1, texCoord, g_vTexCoordScale1, biplanar,
        g_fTintMaskContrast1, g_fTintMaskBrightness1, g_fTextureRoughnessContrast1, g_fTextureRoughnessBrightness1,
        g_bMetalness1, g_mTextureAdjust1, g_mTextureColorAdjust1, g_nColorCorrectionMode1,
        vTintColor_ModelAmount, g_bModelTint1);

    vec3 normalTs = LayerNormal(layer.EncodedNormal, g_tNormal1, g_nUVSet1, g_vTexCoordScale1,
        g_fTextureNormalContrast1, g_flTexCoordRotation1, biplanar, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

#if (F_DETAIL_NORMAL == 1)
    normalTs = ApplyDetailNormal(normalTs, g_tNormalDetail1, DetailUvSet(g_nDetailUVSet1, g_nUVSet1),
        vDetailTexCoords, g_vDetailTexCoordScale1, g_fDetailTextureNormalContrast1,
        g_flDetailTexCoordRotation1, biplanar,
        mat.GeometricNormal, mat.Tangent, mat.Bitangent);
#endif

    // The vertex color mode is ignored without layers
    vec3 albedo = layer.Albedo * mix(vec3(1.0), vVertexColorOut.rgb, layer.Height.g);
    float occlusion = layer.Occlusion;

#if (F_ALPHA_TEST == 1)
    occlusion = layer.Height.b;
    mat.Opacity = AlphaTestAntiAliasing(layer.Occlusion, texCoord, g_flAlphaTestReference, g_flAntiAliasedEdgeStrength);
#endif

    occlusion = LayerOcclusion(occlusion, g_vAmbientOcclusionLevels1);

    InitHeightMaterial(mat, albedo, occlusion, normalTs, layer.Roughness, layer.Height.r, layer.Height.a);

    float reflectance = g_flReflectance;
    ApplyWetnessOld(mat, layer.Height.r, vColorBlendValues.z, vColorBlendValues.w, g_flWetnessDarkeningStrength1, reflectance);

    FinishHeightMaterial(mat, reflectance);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendValues = vColorBlendValues.rgb;
    RenderSurface(g_tColor1, options);
}
