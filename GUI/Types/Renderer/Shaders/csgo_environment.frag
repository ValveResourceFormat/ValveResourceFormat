#version 460

// Includes
#include "common/utils.glsl"
#include "common/rendermodes.glsl"

// Render modes -- Switched on/off by code
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_PBR 0
#define renderMode_Cubemaps 0
#define renderMode_Irradiance 0
#define renderMode_VertexColor 0
#define renderMode_Terrain_Blend 0

#include "common/features.glsl"
#include "csgo_environment.features"

#if (LightmapGameVersionNumber == 0)
    #define S_SPECULAR 0 // No cubemaps in non-lightmapped scenes
#else
    #define S_SPECULAR 1 // Indirect
#endif

#define HemiOctIsoRoughness_RG_B 0

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord;
in vec4 vVertexColor;

#if (F_SECONDARY_UV == 1)
    in vec2 vTexCoord2;
#endif

out vec4 outputColor;

// Material 1
uniform sampler2D g_tColor1;
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
    uniform sampler2D g_tNormalDetail1;
#endif

#if (F_SECONDARY_AO == 1)
    uniform sampler2D g_tSecondaryAO;
#endif

uniform bool g_bModelTint1 = true;
uniform float g_flHeightMapScale1 = 0;
uniform float g_flHeightMapZeroPoint1 = 0;

// Material 2
#if defined(csgo_environment_blend_vfx)
    in vec4 vColorBlendValues;

    uniform sampler2D g_tColor2;
    uniform sampler2D g_tHeight2;
    uniform sampler2D g_tNormal2;

    #if (F_DETAIL_NORMAL == 1)
        uniform sampler2D g_tNormalDetail2;
    #endif

    #if (F_SHARED_COLOR_OVERLAY == 1)
        //uniform sampler2D g_tSharedColorOverlay;
    #endif

    #if (F_BLEND_EFFECTS > 0)
        // ...
    #endif

    uniform bool g_bModelTint2 = true;
    uniform float g_flHeightMapScale2 = 1.0;
    uniform float g_flHeightMapZeroPoint2 = 0.0;

    vec2 GetBlendWeights(vec2 heightTex, vec2 heightScale, vec2 heightZero, vec4 terrainBlend)
    {
        float blendFactor = terrainBlend.x;
        float blendSoftness = terrainBlend.w;

        // Weight calculations
        float h1 = heightScale.x + blendSoftness;
        float height1 = (heightTex.x - heightZero.x) * h1;
        float h2 = heightScale.y + blendSoftness;
        float h22 = (heightTex.y - heightZero.y) * (heightScale.y - blendSoftness);

        float blend1 = (-heightZero.x * h1 - ((1.0 - heightZero.y) * h2)) - blendSoftness;
        float blend2 = (1.0 - heightZero.x) * h1 - ((-heightZero.y) * h2);

        float h2x = h22 + mix(blend1, blend2, blendFactor);

        vec2 weights = vec2(height1, h2x) - vec2(max(height1, h2x) - blendSoftness);
        
        // Clamp negative values
        weights = max(weights, vec2(0.0));

        // Bias towards material 1
        weights.x += 0.001;

        // Normalize
        weights = weights / vec2(weights.x + weights.y);

        return weights;
    }
#endif

#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference = 0.5;
#endif

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"

#include "common/lighting_common.glsl"
#include "common/texturing.glsl"
#include "common/pbr.glsl"

#if (S_SPECULAR == 1 || renderMode_Cubemaps == 1)
#include "common/environment.glsl"
#endif

#include "common/fog.glsl"

// Must be last
#include "common/lighting.glsl"

// Get material properties (possibly from two layers)
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals);

    vec4 color = texture(g_tColor1, texCoord);
    vec4 height = texture(g_tHeight1, texCoord);
    vec4 normal = texture(g_tNormal1, texCoord);
    #if (F_DETAIL_NORMAL == 1)
        vec2 detailNormal = texture(g_tNormalDetail1, vDetailTexCoords).rg;
    #endif

    color.rgb = pow(color.rgb, gamma);

    vec3 tintFactor = 1.0 - height.g * (1.0 - vVertexColor.rgb);
    color.rgb *= (g_bModelTint1) ? tintFactor : vec3(1.0);

    // Blending
#if defined(csgo_environment_blend_vfx)
    vec4 color2 = texture(g_tColor2, vTexCoord.zw);
    vec4 height2 = texture(g_tHeight2, vTexCoord.zw);
    vec4 normal2 = texture(g_tNormal2, vTexCoord.zw);
    #if (F_DETAIL_NORMAL == 1)
        vec2 detailNormal2 = texture(g_tNormalDetail2, vDetailTexCoords).rg;
    #endif

    color2.rgb = pow(color2.rgb, gamma);
    color.rgb *= (g_bModelTint2) ? tintFactor : vec3(1.0);

    vec2 weights = GetBlendWeights(
        vec2(height.r, height2.r),
        vec2(g_flHeightMapScale1, g_flHeightMapScale2),
        vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
        vColorBlendValues
    );

    color = color * weights.x + color2 * weights.y;
    height = height * weights.x + height2 * weights.y;
    normal = normal * weights.x + normal2 * weights.y;
#endif

    mat.Albedo = color.rgb;
    mat.Opacity = color.a;

    // Alpha test
#if (F_ALPHA_TEST == 1)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord);

    if (mat.Opacity - 0.001 < g_flAlphaTestReference)   discard;
#endif

    // Normals and Roughness
    mat.NormalMap = DecodeNormal(normal);

    mat.RoughnessTex = normal.b;

    // Detail texture
#if (F_DETAIL_NORMAL == 1)
    // this is wrong. todo
    mat.NormalMap.xy = (mat.NormalMap.xy + detailNormal) * vec2(0.5);
#endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Metalness = height.a;
    mat.AmbientOcclusion = color.a;

    mat.Roughness = AdjustRoughnessByGeometricNormal(mat.RoughnessTex, mat.GeometricNormal);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

#if (F_SHARED_COLOR_OVERLAY == 1)
    //mat.Albedo = ApplyDecalTexture(texture(g_tSharedColorOverlay, texCoord));
#endif

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    const vec3 F0 = vec3(0.04);
	mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

// MAIN

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);
    vec2 texCoord = vTexCoord.xy;

    // Get material
    MaterialProperties_t mat = GetMaterial(texCoord, vertexNormal);

    LightingTerms_t lighting = InitLighting();

    outputColor = vec4(mat.Albedo, mat.Opacity);

    CalculateDirectLighting(lighting, mat);
    CalculateIndirectLighting(lighting, mat);

    // Combining pass
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor.rgb = pow(combinedLighting, invGamma);

#if defined(csgo_environment_blend_vfx)
    //outputColor.rgb = mat.Albedo;
    //outputColor.rgb = texture(g_tHeight2, texCoord).rgb;
#endif

#if renderMode_FullBright == 1
    vec3 fullbrightLighting = CalculateFullbrightLighting(mat.Albedo, mat.Normal, mat.ViewDir);
    outputColor = vec4(pow(fullbrightLighting, invGamma), mat.Opacity);
#endif

#if renderMode_Color == 1
    outputColor = vec4(pow(mat.Albedo, invGamma), 1.0);
#endif

#if renderMode_BumpMap == 1
    outputColor = vec4(PackToColor(mat.NormalMap), 1.0);
#endif

#if renderMode_Tangents == 1
    outputColor = vec4(PackToColor(mat.Tangent), 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(PackToColor(mat.GeometricNormal), 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(PackToColor(mat.Normal), 1.0);
#endif

#if (renderMode_Diffuse == 1)
    outputColor.rgb = pow(diffuseLighting * 0.5, invGamma);
#endif

#if (renderMode_Specular == 1)
    outputColor.rgb = pow(specularLighting, invGamma);
#endif

#if renderMode_PBR == 1
    outputColor = vec4(mat.AmbientOcclusion, GetIsoRoughness(mat.Roughness), mat.Metalness, 1.0);
#endif

#if (renderMode_Cubemaps == 1)
    // No bumpmaps, full reflectivity
    vec3 viewmodeEnvMap = GetEnvironment(mat, lighting).rgb;
    outputColor.rgb = pow(viewmodeEnvMap, invGamma);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(pow(lighting.DiffuseDirect + lighting.SpecularDirect, invGamma), 1.0);
#endif

#if renderMode_Irradiance == 1 && (F_GLASS == 0)
    outputColor = vec4(pow(lighting.DiffuseIndirect, invGamma), 1.0);
#endif

#if renderMode_VertexColor == 1
    outputColor = vVertexColor;
#endif

#if renderMode_Terrain_Blend == 1 && defined(csgo_environment_blend_vfx)
    outputColor.rgb = vColorBlendValues.rga;
#endif
}
