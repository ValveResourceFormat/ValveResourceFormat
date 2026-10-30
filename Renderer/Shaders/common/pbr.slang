#version 460

#ifndef VRF_PBR_INCLUDE
#define VRF_PBR_INCLUDE

#include "shared_combos.slang"
#include "utils.slang"
#include "texturing.slang"
#include "lighting_common.slang"
#include "shadowmapping.slang"
#include "depth.slang"

#if (F_USE_NPR_LIGHTING == 1)
    #include "lighting_npr.slang"
#endif

// DIFFUSE LIGHTING

#ifndef DIFFUSE_SOFT_NORMAL
    #define DIFFUSE_SOFT_NORMAL 0 // Direct diffuse is lit by mat.SoftNormal instead of mat.Normal
#endif
#ifndef GLASS_BACKLIGHT
    #define GLASS_BACKLIGHT 0 // Light from behind the geometric normal passes through, filtered by mat.TransmissiveColor
#endif

#if (DIFFUSE_WRAP)
uniform float g_flDiffuseExponent = 1.0;
uniform float g_flDiffuseWrap = 1.0;

vec3 DiffuseWrapped(vec3 vNormal, vec3 vLightVector, vec3 wrapColor)
{
    float NoL = dot(vNormal, vLightVector);
    float diffuseWrapDenom = max((NoL + g_flDiffuseWrap) / (1.0 + g_flDiffuseWrap), 0.0001);
    float DiffuseWrapLighting = pow(diffuseWrapDenom, g_flDiffuseExponent) * (1.0 + g_flDiffuseExponent) / (2.0 + 2.0 * g_flDiffuseWrap);
    return mix(vec3(saturate(NoL)), vec3(DiffuseWrapLighting), wrapColor);
}
#endif

// Normal Distribution function --------------------------------------
// D = Normal distribution (Distribution of the microfacets)

// No 1/PI unlike most GGX, s2 lights probably carry it instead. Diffuse is NoL rather than albedo/PI,
// so lightColor is really E/PI and specular owes a PI back, which omitting the divide here repays.
float D_GGX(float NoH, float roughness)
{
	float oneMinusNoHSquared = 1.0 - pow2(NoH);
	float a = NoH * roughness;
	float k = min(roughness / (oneMinusNoHSquared + pow2(a)), 453.5); // 453.5 prevents fp16 overflow
	float d = k * k;
	return d;
}

#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))

float D_AnisoGGX(vec2 roughness, vec3 halfVector, vec3 normal, vec3 tangent, vec3 bitangent)
{
    vec2 alpha = pow2(roughness);

    vec3 Dots;
    Dots.x = dot(halfVector, tangent);
    Dots.y = dot(halfVector, bitangent);
    Dots.z = dot(halfVector, normal);

    Dots /= vec3(alpha, 1.0);
    float ndf = pow2(length_squared(Dots)) * alpha.x * alpha.y;
    return 1.0 / max(ndf, 0.0001); // not normalised to match D_GGX, see the note there
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

// Height-correlated Smith with the cheap approximation of its square roots
float V_SmithGGXCorrelated(float NoL, float NoV, float perceptualRoughness)
{
    float alpha = pow2(perceptualRoughness);
    float visL = NoL * (NoV * (1.0 - alpha) + alpha);
    float visV = NoV * (NoL * (1.0 - alpha) + alpha);
    return 0.5 / max(visL + visV, 0.00001);
}

float GetSpecularVisibility(float NoL, float NoV, float perceptualRoughness)
{
#if (SPECULAR_HEIGHT_CORRELATED_VISIBILITY)
    return V_SmithGGXCorrelated(NoL, NoV, perceptualRoughness);
#else
    return G_SchlickSmithGGX(NoL, NoV, perceptualRoughness);
#endif
}

// Fresnel function ----------------------------------------------------
// F = Fresnel factor (Reflectance depending on angle of incidence)
vec3 F_Schlick(float cosTheta, vec3 F0)
{
	return mix(F0, vec3(1.0), pow(1.0 - cosTheta, 5.0));
}

vec3 GetRetroReflectiveNormal(float retroReflectivity, vec3 L, vec3 V, vec3 N)
{
    vec3 retroReflectionVector = L - reflect(-V, N);
    return normalize(mix(N, retroReflectionVector, retroReflectivity));
}

uniform int F_CLOTH_SHADING; // csgo_character, csgo_complex, csgo_customglove, csgo_textile_layer, vr_complex

#if (SECOND_SPECULAR_LOBE)
    uniform float g_flSecondSpecularLobeGlossiness = 0.5;
    uniform float g_flSecondSpecularLobeRatio = 0.15;
#endif
#if (SUBSURFACE)
    uniform sampler2D g_tDiffuseFalloff; // SrgbRead(true)
    uniform float g_flCurvatureScale = 1.0;

    #if (SUBSURFACE_SKIN)
        uniform vec3 g_vAmbientNormalSoftness = vec3(1.0, 0.3, 0.0);
    #else
        uniform vec3 g_vNormalSoftness = vec3(1.0, 0.3, 0.0);
    #endif

    vec3 GetNormalSoftness()
    {
    #if (SUBSURFACE_SKIN)
        return vec3(1.0, 0.3, 0.0);
    #else
        return g_vNormalSoftness;
    #endif
    }
#endif

// BRDF. Layer 1 is the GGX split sum, layer 2 the sheen directional albedo; layer 0 is unused.
uniform sampler2DArray g_tBRDFLookup; // reserved

#define BRDF_LUT_GGX 1.0
#define BRDF_LUT_SHEEN 2.0

// Half a texel in from each edge of the 64 wide table: ends sample texel centres, not clamped edges.
vec4 SampleBrdfLut(float roughness, float NdotV, float layer)
{
    vec2 coords = vec2(roughness, sqrt(1.0 - NdotV)) * (63.0 / 64.0) + (1.0 / 128.0);
    return textureLod(g_tBRDFLookup, vec3(coords, layer), 0.0);
}

// The games do not floor 1 - NoH^2, which only differs where NoH reaches 1
float D_Charlie(float roughness, float NoH)
{
    float invRough = 1.0 / roughness;
    return (invRough + 2.0) * pow(max(1.0 - pow2(NoH), 0.0078125), invRough * 0.5) / TAU;
}

// Clamped to 1. Unclamped it runs away to ~65000
// where NoL and NoV both approach zero. The games instead keep NoV at least 0.001.
float V_Neubelt(float NoL, float NoV)
{
    return saturate(1.0 / max(4.0 * (NoL + NoV - NoL * NoV), 0.00001532));
}

const float CLOTH_ENERGY_SCALE = PI;

// The vr shaders take the cloth lobe at the perceptual roughness rather than its square, and leave
// out the cosine the other lobes are scaled by a second time
#define CLOTH_VR_LOBE (VR_SHADING)

float GetClothRoughness(float perceptualRoughness)
{
    const float MIN_CLOTH_ROUGHNESS = 1e-4;

    #if (CLOTH_VR_LOBE)
        return max(perceptualRoughness, MIN_CLOTH_ROUGHNESS);
    #else
        return max(pow2(perceptualRoughness), MIN_CLOTH_ROUGHNESS);
    #endif
}

const float MIN_GGX_ALPHA = 1e-4;

void ApplyRetroReflection(inout vec3 normal, inout vec3 halfVector, vec3 lightVector, MaterialProperties_t mat)
{
#if (VR_SHADING)
    // The half vector turns towards the normal as the light comes from the viewer
    halfVector = normalize(mix(halfVector, lightVector - reflect(mat.ViewDir, normal), mat.RetroReflectivity));
#else
    // Unlike the game, which blends in a second lobe around the bent half vector, this bends the normal
    normal = GetRetroReflectiveNormal(mat.RetroReflectivity, lightVector, mat.ViewDir, normal);
#endif
}

float GetBaseNdfVis(MaterialProperties_t mat, vec3 normal, vec3 halfVector, float NoL, float NoV, float NoH)
{
#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
    // Anisotropic shading
    float NDF = D_AnisoGGX(mat.Roughness, halfVector, normal, mat.AnisotropicTangent, mat.AnisotropicBitangent);
    float Vis = GetSpecularVisibility(NoL, NoV, max(mat.Roughness.x, mat.Roughness.y));
#else
    float roughness = max(pow2(mat.IsometricRoughness), MIN_GGX_ALPHA); // linear
    float NDF = D_GGX(NoH, roughness);
    float Vis = GetSpecularVisibility(NoL, NoV, mat.IsometricRoughness);
#endif

    return NDF * Vis;
}

float BlendSecondSpecularLobe(float ndfVis, MaterialProperties_t mat, float NoL, float NoV, float NoH)
{
#if (SECOND_SPECULAR_LOBE)
    // Glossiness is authored inverted. The game floors the scaled roughness by the geometric roughness, not the base
    float flSecondPerceptualRoughness = max(mat.RoughnessTex.x * (1.0 - g_flSecondSpecularLobeGlossiness), mat.GeometricRoughness);
    float flSecondRoughness = max(pow2(flSecondPerceptualRoughness), MIN_GGX_ALPHA);
    float flSecondNdfVis = D_GGX(NoH, flSecondRoughness) * GetSpecularVisibility(NoL, NoV, flSecondPerceptualRoughness);

    return mix(ndfVis, flSecondNdfVis, g_flSecondSpecularLobeRatio);
#else
    return ndfVis;
#endif
}

float GetClothNdfVis(MaterialProperties_t mat, float NoL, float NoV, float NoH)
{
    float ClothNDF = D_Charlie(GetClothRoughness(mat.IsometricRoughness), NoH);
    float ClothVis = V_Neubelt(NoL, NoV);

#if (CLOTH_VR_LOBE)
    return ClothNDF * ClothVis * CLOTH_ENERGY_SCALE;
#else
    return ClothNDF * ClothVis * CLOTH_ENERGY_SCALE * NoL;
#endif
}

vec3 SpecularBlinnPhong(vec3 lightVector, vec3 normal, MaterialProperties_t mat)
{
    float NoL = saturate(dot(normal, lightVector));

    vec3 halfVector = normalize(mat.ViewDir + lightVector);
    float NoH = saturate(dot(normal, halfVector));
    float VoH = saturate(dot(lightVector, halfVector));

    vec2 exponents = exp2(pow(vec2(1.0) - mat.Roughness, vec2(1.5)) * 14.0);
    float exponent = dot(exponents, vec2(0.5));
    float smoothness = dot(vec2(1.0) - saturate(mat.Roughness * 0.5), vec2(1.0 / 3.0));
    float normalization = ((exponent + 2.0) * (exponent + 4.0)) / (8.0 * (exp2(exponent * -0.5) + exponent));

#if (SPECULAR_FRESNEL_TINT_AFTER)
    vec3 metalTint = mix(vec3(1.0), mat.Albedo, mat.Metalness);
    float grazing = pow(1.0 - VoH, mat.FresnelExponent);
    vec3 F = vec3(mat.Reflectance + (1.0 - mat.Reflectance) * grazing) * metalTint;
#else
    vec3 F = F_Schlick(VoH, mat.SpecularColor);
#endif

    return F * (pow(NoH, exponent) * smoothness * NoL * normalization);
}

// Still pass normal due to some effects modifying normal per light
vec3 SpecularLighting(vec3 lightVector, vec3 normal, MaterialProperties_t mat, float sheenAlbedoScaling)
{
#if (SPECULAR_BLINN_PHONG)
    return SpecularBlinnPhong(lightVector, normal, mat);
#else
    float NoL = saturate(dot(normal, lightVector));

    vec3 halfVector = normalize(mat.ViewDir + lightVector);

    if (F_RETRO_REFLECTIVE == 1)
    {
        ApplyRetroReflection(normal, halfVector, lightVector, mat);
    }

    float NoH = saturate(dot(normal, halfVector));
    float NoV = saturate(dot(normal, mat.ViewDir));
    float VoH = saturate(dot(lightVector, halfVector));

    vec3 F = F_Schlick(VoH, mat.SpecularColor);
    float ndfVis = GetBaseNdfVis(mat, normal, halfVector, NoL, NoV, NoH);
    ndfVis = BlendSecondSpecularLobe(ndfVis, mat, NoL, NoV, NoH);

    if (F_CLOTH_SHADING == 1)
    {
        ndfVis = mix(ndfVis, GetClothNdfVis(mat, NoL, NoV, NoH), mat.ClothMask);
    }

    // Sheen is a second lobe with its own colour and roughness, not a blend into the base,
    // so the base gives up the energy it takes.
    vec3 specular = vec3(ndfVis) * NoL * F * sheenAlbedoScaling;

    if (mat.SheenRoughness > 0.0)
    {
        float sheenAlpha = pow2(mat.SheenRoughness);
        specular += mat.SheenColor * (D_Charlie(sheenAlpha, NoH) * V_Neubelt(NoL, NoV) * NoL);
    }

    return specular;
#endif
}

// Base lobe energy left after the sheen. The directional albedo is read at light and view, as a
// grazing sheen absorbs from either. Without a sheen lobe this is 1 and nothing is sampled.
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

    float sheenMax = Max3(mat.SheenColor);
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

#if (SUBSURFACE)
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

    #if (SUBSURFACE_SKIN)
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
        vec3 vChannelNormal = normalize(mix(mat.Normal, vSoftNormal, mix(GetNormalSoftness()[i], 1.0, vScatter[i])));
        vChannelNoL[i] = saturate(dot(vChannelNormal, lightVector));
    }

    vec3 vFalloffSample = textureLod(g_tDiffuseFalloff, vec2(flSoftNoL * 0.5 + 0.5, flCurvature), 0.0).rgb;

    #if (SUBSURFACE_FALLOFF_RAW)
        vFalloffSample = SrgbLinearToGamma(vFalloffSample);
    #endif

    vec3 vFalloff = vFalloffSample * 0.5 - 0.25;

    return mix(diffuseLight, saturate(vChannelNoL + vFalloff), mat.SSSMask);
}
#else
vec3 GetSubsurfaceDiffuse(MaterialProperties_t mat, vec3 lightVector, vec3 diffuseLight)
{
    return diffuseLight;
}
#endif

// A cosine lobe that narrows as the surface gets smoother, normalized to keep its energy
float GetRoughnessShapedDiffuse(float NoL, MaterialProperties_t mat)
{
    vec2 exponents = (vec2(1.0) - mat.Roughness) * 0.8 + 0.6;
    float exponent = (exponents.x + exponents.y) * 0.5;

    return (exponent + 1.0) * 0.5 * pow(NoL, exponent);
}

vec3 GetDiffuseNormal(MaterialProperties_t mat)
{
#if (DIFFUSE_SOFT_NORMAL)
    return mat.SoftNormal;
#else
    return mat.Normal;
#endif
}

vec3 GetBaseDiffuse(MaterialProperties_t mat, vec3 diffuseNormal, vec3 lightVector, bool roughnessShapedDiffuse)
{
#if (F_USE_NPR_LIGHTING == 1)
    return vec3(NprDirectDiffuse(saturate(dot(diffuseNormal, lightVector))));
#elif (DIFFUSE_WRAP)
    return DiffuseWrapped(diffuseNormal, lightVector, mat.DiffuseWrapColor);
#else
    float NoL = ClampToPositive(dot(diffuseNormal, lightVector));
    return vec3(roughnessShapedDiffuse ? GetRoughnessShapedDiffuse(NoL, mat) : NoL);
#endif
}

void AddTransmissiveLight(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    #if (TRANSMISSIVE_GEOMETRIC_NORMAL)
        vec3 transmissiveNormal = mat.GeometricNormal;
    #else
        vec3 transmissiveNormal = mat.Normal;
    #endif

    float transmissiveLight = ClampToPositive(-dot(transmissiveNormal, lightVector)); // Light passing through the back face
    lighting.TransmissiveDirect += transmissiveLight * lightColor * mat.TransmissiveColor;
#endif

#if (GLASS_BACKLIGHT)
    float backLight = ClampToPositive(-dot(mat.GeometricNormal, lightVector));
    lighting.TransmissiveDirect += backLight * lightColor * mat.TransmissiveColor;
#endif

#if (SUBSURFACE_SKIN)
    float flTransmissionWrap = saturate(0.5 - 0.5 * dot(mat.GeometricNormal, lightVector));
    float flTransmissionDepth = CalculateSunShadowThickness(mat.PositionWS) * g_flTransmissionFalloff;

    lighting.TransmissiveDirect += exp(-pow2(flTransmissionDepth)) * flTransmissionWrap * lightColor * mat.TransmissiveColor;
#endif
}

// Calculate PBR shading for a light
void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat, bool roughnessShapedDiffuse)
{
    vec3 diffuseLight = GetBaseDiffuse(mat, GetDiffuseNormal(mat), lightVector, roughnessShapedDiffuse);
    diffuseLight = GetSubsurfaceDiffuse(mat, lightVector, diffuseLight);

    float sheenAlbedoScaling = GetSheenAlbedoScaling(mat, lightVector);
    diffuseLight *= sheenAlbedoScaling;

    vec3 specularLight = SpecularLighting(lightVector, mat.Normal, mat, sheenAlbedoScaling);

#if (LIGHT_GEOMETRIC_NORMAL_GATE) && (GLASS_BACKLIGHT)
    // A light behind the geometric normal passes through the surface, and only that light shows
    bool lightFacesSurface = dot(lightVector, mat.GeometricNormal) > 0.0;
#else
    bool lightFacesSurface = true;
#endif

    if (lightFacesSurface)
    {
        lighting.SpecularDirect += specularLight * lightColor;
        lighting.DiffuseDirect += diffuseLight * lightColor;
    }

    AddTransmissiveLight(lighting, lightVector, lightColor, mat);
}

void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
    CalculateShading(lighting, lightVector, lightColor, mat, false);
}

#endif
