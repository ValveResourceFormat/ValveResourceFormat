#version 460

#include "csgo_foliage_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
#define LIGHTMAP_NON_DIRECTIONAL 1
#define TRANSMISSIVE_GEOMETRIC_NORMAL 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_foliage.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tAmbientOcclusion;

uniform mat4 g_mTextureColorAdjust = mat4(1.0); // evaluated on the CPU from g_fTextureColor* params
uniform float g_fTextureNormalContrast = 1.0;
uniform float g_fTextureRoughnessContrast = 1.0;
uniform float g_fTextureRoughnessBrightness = 1.0;
uniform float g_flHeightGradientAmount = 0.0;
uniform float g_flHeightGradientPower = 1.0;

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    uniform mat4 g_mTransmissiveTextureColorAdjust = mat4(1.0); // evaluated on the CPU like g_mTextureColorAdjust
    uniform bool g_bApplyColorCorrectionToTransmissiveColor = false;
#endif

uniform float g_flAlphaTestReference = 0.5;

#if (F_ALPHA_TEST == 1)
    uniform float g_flGrazingAngleMask = 0.0;
    uniform float g_flAlphaBoostStrength = 3.0;
    uniform float g_flAlphaBoostDistance = 20000.0;

    // Keeps the coverage of surfaces seen edge on, and thickens the far ones whose alpha the mips thin out
    float GetAlphaTestCoverageScale(MaterialProperties_t mat)
    {
        vec3 faceNormal = normalize(cross(dFdx(mat.PositionWS), dFdy(mat.PositionWS)));
        float grazing = SafePow(abs(dot(mat.ViewDir, faceNormal)), g_flGrazingAngleMask);
        float boost = mix(1.0, g_flAlphaBoostStrength, saturate(distance(g_vCameraPositionWs, mat.PositionWS) / g_flAlphaBoostDistance));

        return grazing * boost;
    }
#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    float alpha = color.a;
#if (F_ALPHA_TEST == 1)
    alpha *= GetAlphaTestCoverageScale(mat);
#endif
    InitOpacity(mat, alpha, 1.0, false, false, texCoord, g_flAlphaTestReference);

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).x);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif

    mat.Albedo = max((vec4(mat.Albedo, 1.0) * g_mTextureColorAdjust).rgb, vec3(0.0));
    normalTexture.b = ContrastBrightness(normalTexture.b, g_fTextureRoughnessContrast, g_fTextureRoughnessBrightness);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.NormalMap.xy *= g_fTextureNormalContrast;
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;
    ApplyFoliageOcclusion(mat);

    ApplyTransmissiveColor(mat, texCoord);

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    // The albedo is already tinted, the transmissive texture gets the same tint here
    if (F_USE_ALBEDO_FOR_TRANSMISSIVE == 0)
    {
        if (g_bApplyColorCorrectionToTransmissiveColor)
        {
            mat.TransmissiveColor = (vec4(mat.TransmissiveColor, 1.0) * g_mTransmissiveTextureColorAdjust).rgb;
        }

        mat.TransmissiveColor *= vVertexColorOut.rgb;
    }
#endif

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);
    ResolveDiffuseSpecular(mat, vec3(0.04));

    // Red vertex color brightens the diffuse color towards the top of the plant
    float heightGradient = pow(clamp(vFoliageParamsOut.x, 0.001, 1.0), g_flHeightGradientPower);
    mat.DiffuseColor *= 1.0 + g_flHeightGradientAmount * heightGradient;

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    if (RenderSurface(g_tColor, options))
    {
        return;
    }

#if (S_MODE_DEPTH == 0)
    if (g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = vFoliageParamsOut.rgb;
    }
#endif
}
