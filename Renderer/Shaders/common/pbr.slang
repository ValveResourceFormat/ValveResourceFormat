#version 460

#ifndef VRF_PBR_INCLUDE
#define VRF_PBR_INCLUDE

#include "features.slang"
#include "utils.slang"
#include "texturing.slang"
#include "lighting_common.slang"

// DIFFUSE LIGHTING

#if (F_DIFFUSE_WRAP == 1) || (GameVfx_vr_xen_foliage != 0)
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
uniform int F_SUBSURFACE_SCATTERING; // csgo_character, vr_skin

// Preintegrated skin. Indexed by NdotL across and curvature down, storing a signed correction to the
// diffuse term rather than the term itself. Authored per material (TextureDiffuseFalloff, suffix
// "diffusewarp"), not engine supplied - CS2 ships one for skin and others for fleece and cake icing.
uniform sampler2D g_tDiffuseFalloff; // SrgbRead(true)
uniform vec3 g_vNormalSoftness = vec3(0.0);
uniform float g_flCurvatureScale = 1.0;

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
    // csgo_character leaves the base unclamped. The 2^-7 floor is kept: it stops the pow running at
    // exactly zero with an exponent that reaches into the thousands as roughness approaches its own
    // minimum, and only moves the result where the lobe has already fallen to nothing.
    return (invRough + 2.0) * pow(max(1.0 - pow2(NoH), 0.0078125), invRough * 0.5) / TAU;
}

// Clamped to 1 (Filament, Deadlock). Unclamped it runs away to ~65000
// where NoL and NoV both approach zero. csgo_character instead biases NoV by +0.001; this guard is
// kept because it bounds the same blow-up without pushing NoV off its true value.
float V_Neubelt(float NoL, float NoV)
{
    return saturate(1.0 / max(4.0 * (NoL + NoV - NoL * NoV), 0.00001532));
}

// csgo_character folds D_Charlie * V_Neubelt down to a single 1/8. D_Charlie's 1/TAU and
// V_Neubelt's 1/4 above come to 1/(8*PI) against that, so the cloth lobe scales by PI to land on
// Valve's constant. The sheen lobe keeps Filament's normalisation and is not scaled.
#define CLOTH_ENERGY_SCALE PI

// Still pass normal due to some effects modifying normal per light
vec3 specularLighting(vec3 lightVector, vec3 normal, MaterialProperties_t mat, float sheenAlbedoScaling)
{
	float NoL = saturate( dot(normal, lightVector) );

	vec3 halfVector = normalize(mat.ViewDir + lightVector);

    if (F_RETRO_REFLECTIVE == 1)
    {
        normal = GetRetroReflectiveNormal(mat.ExtraParams.r, lightVector, mat.ViewDir, normal, halfVector);
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

    if (F_CLOTH_SHADING == 1)
    {
        float ClothNDF = D_Charlie(roughness, NoH);
        float ClothVis = V_Neubelt(NoL, NoV);
        // NoL twice on purpose: the cloth lobe carries one inside the blend and the shared multiply
        // below applies another. Both of csgo_character's light loops emit it that way.
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

// What a single GGX bounce loses to multiple scattering, put back on the direct specular. Estimated
// from roughness and NoV rather than read out of the table, so it costs nothing to evaluate; it runs
// once over the accumulated direct specular rather than per light. Cloth has no GGX lobe to
// compensate, so the mask takes it back to 1.
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

// Preintegrated skin, evaluated per light. Light that enters the surface and leaves somewhere else is
// approximated two ways at once: the shading normal is softened per channel, so red - which scatters
// furthest - is lit by the flattest normal, and the falloff table adds back the light that wraps past
// the terminator. Both the scatter amount and the table's NdotL axis are read against the soft
// normal, not the shading normal.
vec3 GetSubsurfaceDiffuse(MaterialProperties_t mat, vec3 lightVector, vec3 diffuseLight)
{
    // Onto the table's V axis. Both constants are read straight out of csgo_character; they work out
    // to a curvature range of roughly [0.025, 2.54], and are not a half texel inset of the 512 tall
    // table, so do not "correct" them to one.
    float flCurvature = (GetCurvature(mat.GeometricNormal, mat.PositionWS) * g_flCurvatureScale) * 0.3976777493953704833984375 - 0.0101010091602802276611328125;

    // The soft end of the blend is the same normal map with its bitangent term negated, so raising
    // softness flattens the normal along the bitangent rather than fading toward the geometric
    // normal. That is what csgo_character blends toward - it builds the vector from the one
    // g_tNormal sample twice, differing only in the sign of tangent space Y.
    vec3 vSoftNormal = normalize(mat.Tangent * mat.NormalMap.x - mat.Bitangent * mat.NormalMap.y + mat.GeometricNormal * mat.NormalMap.z);

    float flSoftNoL = dot(vSoftNormal, lightVector);

    // Facing away from the light the shading normal is given up entirely, which is what rounds off
    // the terminator; facing into it, each channel keeps whatever softness the material asked for.
    vec3 vScatter = saturate(vec3(1.0) - vec3(flSoftNoL));
    vScatter *= vScatter;

    vec3 vChannelNoL;

    for (int i = 0; i < 3; i++)
    {
        vec3 vChannelNormal = normalize(mix(mat.Normal, vSoftNormal, mix(g_vNormalSoftness[i], 1.0, vScatter[i])));
        vChannelNoL[i] = saturate(dot(vChannelNormal, lightVector));
    }

    vec3 vFalloff = textureLod(g_tDiffuseFalloff, vec2(flSoftNoL * 0.5 + 0.5, flCurvature), 0.0).rgb * 0.5 - 0.25;

    return mix(diffuseLight, saturate(vChannelNoL + vFalloff), mat.SSSMask);
}

// Calculate PBR shading for a light
void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
#if defined(S_DIFFUSE_WRAP)
    vec3 diffuseLight = diffuseWrapped(mat.Normal, lightVector);
#else
    vec3 diffuseLight = vec3(ClampToPositive(dot(mat.Normal, lightVector)));
#endif

    if (F_SUBSURFACE_SCATTERING == 1)
    {
        diffuseLight = GetSubsurfaceDiffuse(mat, lightVector, diffuseLight);
    }

    float sheenAlbedoScaling = GetSheenAlbedoScaling(mat, lightVector);
    diffuseLight *= sheenAlbedoScaling;

    vec3 specularLight = specularLighting(lightVector, mat.Normal, mat, sheenAlbedoScaling);

    lighting.SpecularDirect += specularLight * lightColor;
    lighting.DiffuseDirect += diffuseLight * lightColor;

    #if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
        float transmissiveLight = ClampToPositive(-dot(mat.Normal, lightVector)); // Light passing through the back face
        lighting.TransmissiveDirect += transmissiveLight * lightColor * mat.TransmissiveColor;
    #endif
}

#endif
