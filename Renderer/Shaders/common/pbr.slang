#version 460

#ifndef VRF_PBR_INCLUDE
#define VRF_PBR_INCLUDE

#include "features.slang"
#include "utils.slang"
#include "texturing.slang"
#include "lighting_common.slang"
#include "shadowmapping.slang"

// DIFFUSE LIGHTING

#if (diffuseWrap)
// idea: what if we included individual features in tiny files per feature. they would all be used in #includes
#define S_DIFFUSE_WRAP

// Used in vr_xen_foliage, vr_eyeball (not supported yet bc alt param names), and optionally in vr_complex
uniform float g_flDiffuseExponent = 1.0;
uniform float g_flDiffuseWrap = 1.0;
uniform vec3 g_vDiffuseWrapColor = vec3(1.0, 0.5, 0.3); // SrgbRead(true)

vec3 diffuseWrapped(vec3 vNormal, vec3 vLightVector)
{
    float NoL = dot(vNormal, vLightVector);
    float diffuseWrapDenom = max((NoL + g_flDiffuseWrap) / (1.0 + g_flDiffuseWrap), 0.0001);
    float DiffuseWrapLighting = pow(diffuseWrapDenom, g_flDiffuseExponent) * (1.0 + g_flDiffuseExponent) / (2.0 + 2.0 * g_flDiffuseWrap);
    return mix(vec3(saturate(NoL)), vec3(DiffuseWrapLighting), g_vDiffuseWrapColor.rgb);
}
#endif


// Normal Distribution function --------------------------------------
// D = Normal distribution (Distribution of the microfacets)
float D_GGX(float NoH, float roughness)
{
	float oneMinusNoHSquared = 1.0 - pow2(NoH);
	float a = NoH * roughness;
	float k = min(roughness / (oneMinusNoHSquared + pow2(a)), 453.5); // 453.5 prevents fp16 overflow
	float d = k * (k * (1.0 / PI));
	return d;
}

#if defined(ANISO_ROUGHNESS)

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
in vec3 vAnisoBitangentOut;
#endif

void CalculateAnisotropicTangents(inout MaterialProperties_t mat)
{
#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    mat.AnisotropicTangent = normalize(cross(vAnisoBitangentOut, mat.Normal));
    mat.AnisotropicBitangent = normalize(cross(mat.Normal, mat.AnisotropicTangent));
#else
    // Recalculate Tangent/Bitangent based on normal map
    mat.AnisotropicTangent = normalize(cross(mat.Bitangent, mat.Normal));
    mat.AnisotropicBitangent = normalize(cross(mat.Normal, mat.Tangent));
#endif
}

float D_AnisoGGX(vec2 roughness, vec3 halfVector, vec3 normal, vec3 tangent, vec3 bitangent)
{
    vec2 alpha = pow2(roughness);

    vec3 Dots;
    Dots.x = dot(halfVector, tangent);
    Dots.y = dot(halfVector, bitangent);
    Dots.z = dot(halfVector, normal);

    Dots /= vec3(alpha, 1.0);
    float ndf = pow2(dot(Dots, Dots)) * alpha.x * alpha.y;
    return 1.0 / (PI * max(ndf, 0.0001));
}
#endif

// Geometric Shadowing visibility function --------------------------------------
	// G = Geometric shadowing term (Microfacets shadowing)
float G_SchlickSmithGGX(float NoL, float NoV, float perceptualRoughness)
{
    float VisRough = pow2(perceptualRoughness + 1.0) / 8.0;
    float SchlickVisL = NoL * (1.0 - VisRough) + VisRough;
    float SchlickVisV = NoV * (1.0 - VisRough) + VisRough;
    float Vis = 1.0 / (4.0 * max(SchlickVisL * SchlickVisV, 0.0001));
    return Vis;
}

// Fresnel function ----------------------------------------------------
// F = Fresnel factor (Reflectance depending on angle of incidence)
float F_Schlick(float F0, float F90, float VoH)
{
    return mix(F0, F90, pow(1.0 - VoH, 5.0));
}

vec3 F_Schlick(float cosTheta, vec3 F0)
{
	return mix(F0, vec3(1.0), pow(1.0 - cosTheta, 5.0));
}

vec3 GetRetroReflectiveNormal(float retroReflectivity, vec3 L, vec3 V, vec3 N, vec3 H)
{
    vec3 retroReflectionVector = L - reflect(-V, N);
    return normalize(mix(N, retroReflectionVector, retroReflectivity));
}

uniform int F_CLOTH_SHADING; // csgo_character, csgo_complex, csgo_customglove, csgo_textile_layer, vr_complex

#if (secondSpecularLobe)
    uniform float g_flSecondSpecularLobeGlossiness = 0.423;
    uniform float g_flSecondSpecularLobeRatio = 0.021;
#endif
#if (subsurface)
    uniform sampler2D g_tDiffuseFalloff; // SrgbRead(true)
    uniform float g_flCurvatureScale = 1.0;

    #if (subsurfaceSkin)
        uniform vec3 g_vAmbientNormalSoftness = vec3(0.0);
        #define NORMAL_SOFTNESS g_vAmbientNormalSoftness
    #else
        uniform vec3 g_vNormalSoftness = vec3(0.0);
        #define NORMAL_SOFTNESS g_vNormalSoftness
    #endif
#endif

// BRDF. Layer 1 is the GGX split sum, layer 2 the sheen directional albedo; layer 0 is unused.
uniform sampler2DArray g_tBRDFLookup; // reserved

#define BRDF_LUT_GGX 1.0
#define BRDF_LUT_SHEEN 2.0

// Half a texel in from either edge of the 64 wide table, so the ends of the range are the
// centres of the first and last texels rather than a clamped extrapolation.
vec4 SampleBrdfLut(float roughness, float NdotV, float layer)
{
    vec2 coords = vec2(roughness, sqrt(1.0 - NdotV)) * (63.0 / 64.0) + (1.0 / 128.0);
    return textureLod(g_tBRDFLookup, vec3(coords, layer), 0.0);
}

float D_Charlie(float roughness, float NoH)
{
    float invRough = 1.0 / roughness;
    return (invRough + 2.0) * pow(max(1.0 - pow2(NoH), 0.0078125), invRough * 0.5) / TAU;
}

// Clamped to 1 (Filament, Deadlock). Unclamped it runs away to ~65000
// where NoL and NoV both approach zero.
float V_Neubelt(float NoL, float NoV)
{
    return saturate(1.0 / max(4.0 * (NoL + NoV - NoL * NoV), 0.00001532));
}

#define CLOTH_ENERGY_SCALE PI

// Still pass normal due to some effects modifying normal per light
vec3 specularLighting(vec3 lightVector, vec3 normal, MaterialProperties_t mat, float sheenAlbedoScaling)
{
	float NoL = saturate( dot(normal, lightVector) );

	vec3 halfVector = normalize(mat.ViewDir + lightVector);

    if (F_RETRO_REFLECTIVE == 1)
    {
        normal = GetRetroReflectiveNormal(mat.RetroReflectivity, lightVector, mat.ViewDir, normal, halfVector);
    }

	float NoH = saturate(dot(normal, halfVector));
	float NoV = saturate(dot(normal, mat.ViewDir));
	float VoH = saturate(dot(lightVector, halfVector));
	const float MIN_GGX_ALPHA = 1e-4;
	float roughness = max(pow2(mat.IsometricRoughness), MIN_GGX_ALPHA); // linear

#if defined(ANISO_ROUGHNESS)
    // Anisotropic shading
    float NDF = D_AnisoGGX(mat.Roughness, halfVector, normal, mat.AnisotropicTangent, mat.AnisotropicBitangent);
	float Vis = G_SchlickSmithGGX(NoL, NoV, max(mat.Roughness.x, mat.Roughness.y));
#else
    float NDF = D_GGX(NoH, roughness);
	float Vis = G_SchlickSmithGGX(NoL, NoV, mat.IsometricRoughness);
#endif
	vec3 F = F_Schlick(VoH, mat.SpecularColor);
    float ndfVis = NDF * Vis;

#if (secondSpecularLobe)
    float flSecondPerceptualRoughness = mat.IsometricRoughness * g_flSecondSpecularLobeGlossiness;
    float flSecondRoughness = max(pow2(flSecondPerceptualRoughness), MIN_GGX_ALPHA);
    float flSecondNdfVis = D_GGX(NoH, flSecondRoughness) * G_SchlickSmithGGX(NoL, NoV, flSecondPerceptualRoughness);

    ndfVis = mix(ndfVis, flSecondNdfVis, g_flSecondSpecularLobeRatio);
#endif

    if (F_CLOTH_SHADING == 1)
    {
        float ClothNDF = D_Charlie(roughness, NoH);
        float ClothVis = V_Neubelt(NoL, NoV);
        ndfVis = mix(ndfVis, ClothNDF * ClothVis * CLOTH_ENERGY_SCALE * NoL, mat.ClothMask);
    }

    // Sheen is a second lobe over the top, with its own colour and roughness, rather than a
    // blend into the first, so the base has to give up the energy it takes.
    vec3 specular = vec3(ndfVis) * NoL * F * sheenAlbedoScaling;

    if (mat.SheenRoughness > 0.0)
    {
        float sheenAlpha = pow2(mat.SheenRoughness);
        specular += mat.SheenColor * (D_Charlie(sheenAlpha, NoH) * V_Neubelt(NoL, NoV) * NoL);
    }

    return specular;
}




// How much of the base lobes survives once the sheen has taken its share. The directional
// albedo is read at both the light and the view, because a grazing sheen absorbs from either
// direction. Without a sheen lobe this is 1 and nothing is sampled.
float GetSheenAlbedoScaling(MaterialProperties_t mat, vec3 lightVector)
{
    if (mat.SheenRoughness <= 0.0)
    {
        return 1.0;
    }

    float NoL = ClampToPositive(dot(mat.Normal, lightVector));
    float NoV = ClampToPositive(dot(mat.Normal, mat.ViewDir));

    float directionalAlbedo = max(SampleBrdfLut(mat.SheenRoughness, NoV, BRDF_LUT_SHEEN).x,
                                  SampleBrdfLut(mat.SheenRoughness, NoL, BRDF_LUT_SHEEN).x);

    float sheenMax = max(mat.SheenColor.x, max(mat.SheenColor.y, mat.SheenColor.z));
    return ClampToPositive(1.0 - sheenMax * directionalAlbedo);
}

vec3 GetSpecularEnergyCompensation(MaterialProperties_t mat)
{
    float flRoughnessSum = mat.Roughness.x + mat.Roughness.y;
    float flBoost = 0.125 * pow2(pow2(flRoughnessSum)) * saturate(dot(mat.AmbientNormal, mat.ViewDir));

    vec3 vCompensation = vec3(1.0) + mat.SpecularColor * flBoost;

    if (F_CLOTH_SHADING == 1)
    {
        vCompensation = mix(vCompensation, vec3(1.0), mat.ClothMask);
    }

    return vCompensation;
}

#if (subsurface)
const float SSS_CURVATURE_RADIUS_MIN_METRES = 0.01;
const float SSS_CURVATURE_RADIUS_MAX_METRES = 1.0;

const float SSS_CURVATURE_MIN = INCH_TO_M / SSS_CURVATURE_RADIUS_MAX_METRES;
const float SSS_CURVATURE_MAX = INCH_TO_M / SSS_CURVATURE_RADIUS_MIN_METRES;

vec3 GetSubsurfaceDiffuse(MaterialProperties_t mat, vec3 lightVector, vec3 diffuseLight)
{
    #if (F_USE_PER_VERTEX_CURVATURE == 1)
        float flCurvature = vCurvatureOut * g_flCurvatureScale;
    #else
        float flCurvature = GetCurvature(mat.GeometricNormal, mat.PositionWS) * g_flCurvatureScale;
    #endif

    flCurvature = RemapValClamped(flCurvature, SSS_CURVATURE_MIN, SSS_CURVATURE_MAX, 0.0, 1.0);

    #if (subsurfaceSkin)
        vec3 vSoftNormal = mat.SoftNormal;
    #else
        vec3 vSoftNormal = normalize(mat.Tangent * mat.NormalMap.x - mat.Bitangent * mat.NormalMap.y + mat.GeometricNormal * mat.NormalMap.z);
    #endif

    float flSoftNoL = dot(vSoftNormal, lightVector);

    vec3 vScatter = saturate(vec3(1.0) - vec3(flSoftNoL));
    vScatter *= vScatter;

    vec3 vChannelNoL;

    for (int i = 0; i < 3; i++)
    {
        vec3 vChannelNormal = normalize(mix(mat.Normal, vSoftNormal, mix(NORMAL_SOFTNESS[i], 1.0, vScatter[i])));
        vChannelNoL[i] = saturate(dot(vChannelNormal, lightVector));
    }

    vec3 vFalloff = textureLod(g_tDiffuseFalloff, vec2(flSoftNoL * 0.5 + 0.5, flCurvature), 0.0).rgb * 0.5 - 0.25;

    return mix(diffuseLight, saturate(vChannelNoL + vFalloff), mat.SSSMask);
}
#endif

// Calculate PBR shading for a light
void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
#if defined(S_DIFFUSE_WRAP)
    vec3 diffuseLight = diffuseWrapped(mat.Normal, lightVector);
#else
    vec3 diffuseLight = vec3(ClampToPositive(dot(mat.Normal, lightVector)));
#endif

    #if (subsurface)
        diffuseLight = GetSubsurfaceDiffuse(mat, lightVector, diffuseLight);
    #endif

    float sheenAlbedoScaling = GetSheenAlbedoScaling(mat, lightVector);
    diffuseLight *= sheenAlbedoScaling;

    vec3 specularLight = specularLighting(lightVector, mat.Normal, mat, sheenAlbedoScaling);

    lighting.SpecularDirect += specularLight * lightColor;
    lighting.DiffuseDirect += diffuseLight * lightColor;

    #if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
        float transmissiveLight = ClampToPositive(-dot(mat.Normal, lightVector)); // Light passing through the back face
        lighting.TransmissiveDirect += transmissiveLight * lightColor * mat.TransmissiveColor;
    #endif

    #if (subsurfaceSkin)
        float flTransmissionWrap = saturate(0.5 - 0.5 * dot(mat.GeometricNormal, lightVector));
        float flTransmissionDepth = CalculateSunShadowThickness(mat.PositionWS) * g_flTransmissionFalloff;

        lighting.TransmissiveDirect += exp(-pow2(flTransmissionDepth)) * flTransmissionWrap * lightColor * mat.TransmissiveColor;
    #endif
}

#endif
