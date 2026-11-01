#version 460

#ifndef VRF_SURFACE_BLEND_2WAY_INCLUDE
#define VRF_SURFACE_BLEND_2WAY_INCLUDE

// Fragment shader body of the lit surfaces that blend two color and normal layers by a mask.
// The entry file includes its features file, sets the lighting profile knobs, includes this, then declares
// vColorBlendValues if it uses it and writes GetBlendFactor() and GetMaterial() from GetTwoWayBlendMaterial(),
// with the bump strength that scales the slope of the normal map while F_SCALE_NORMAL_MAP is off.

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "surface_base_ps.slang"
#include "surface_material.slang"
#include "surface_shading.slang"
#include "../features/blend_second_layer.slang"

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)
uniform sampler2D g_tColorB; // SrgbRead(true)
uniform sampler2D g_tNormalB;
uniform sampler2D g_tMask;
uniform vec2 g_vTexCoordScale2 = vec2(1.0);
uniform float g_flMetalnessA = 0.0;
uniform float g_flMetalnessB = 0.0;

#if (F_ENABLE_TINT_MASKS == 1)
    uniform sampler2D g_tTintMask;
#endif

// The weight of the second layer
float GetBlendFactor(Layer2_t layer2);

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetTwoWayBlendMaterial(vec2 texCoord, vec3 vertexNormals, float bumpStrength)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColorA, texCoord);
    vec4 normalTexture = texture(g_tNormalA, texCoord);

    Layer2_t layer2 = SampleLayer2(g_tColorB, g_tNormalB, texCoord, g_vTexCoordScale2);

    float blendFactor = GetBlendFactor(layer2);

#if (F_ENABLE_TINT_MASKS == 1)
    // Each layer is tinted by its own mask before the blend
    vec2 tintMasks = texture(g_tTintMask, texCoord).xy;

    color.rgb *= GetMaskedVertexTint(tintMasks.x);
    layer2.Color.rgb *= GetMaskedVertexTint(tintMasks.y);
#endif

    BlendLayer2(color, normalTexture, layer2, blendFactor);

    mat.Albedo = color.rgb;
    InitOpacity(mat, color.a, 1.0, false, false, texCoord, g_flAlphaTestReference);

#if (F_ENABLE_TINT_MASKS == 0)
    mat.Albedo *= vVertexColorOut.rgb;
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;

    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, bumpStrength);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);

#if (F_ENABLE_AMBIENT_OCCLUSION == 1)
    mat.AmbientOcclusion = color.a;
#endif

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColorA, options);
}

#endif
