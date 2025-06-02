#version 460
//? #include "features.glsl"
//? #include "utils.glsl"
//? #include "texturing.glsl"
//? #include "lighting_common.glsl"

// DIFFUSE LIGHTING

float diffuseLobe(float NoL, float roughness)
{
    float diffuseExponent = (1.0 - roughness) * 0.8 + 0.6;
    return pow(NoL, diffuseExponent) * ((diffuseExponent + 1.0) / 2.0);
}


#if (F_DIFFUSE_WRAP == 1) || defined(vr_xen_foliage_vfx)
// idea: what if we included individual features in tiny files per feature. they would all be used in #includes
#define S_DIFFUSE_WRAP

// Used in vr_xen_foliage, vr_eyeball (not supported yet bc alt param names), and optionally in vr_complex
uniform float g_flDiffuseExponent = 1.0;
uniform float g_flDiffuseWrap = 1.0;
uniform vec3 g_vDiffuseWrapColor = vec3(1.0, 0.5, 0.3); // 1.0, 0.5, 0.3 -> srgbtolinear

vec3 diffuseWrapped(vec3 vNormal, vec3 vLightVector)
{
    float NoL = dot(vNormal, vLightVector);
    float diffuseWrapDenom = ClampToPositive((NoL + g_flDiffuseWrap) / (1.0 + g_flDiffuseWrap));
    float DiffuseWrapLighting = pow(diffuseWrapDenom, g_flDiffuseExponent) * (1.0 + g_flDiffuseExponent) / (2.0 + 2.0 * g_flDiffuseWrap);
    return mix(vec3(saturate(NoL)), vec3(DiffuseWrapLighting), g_vDiffuseWrapColor.rgb);
}
#endif



// Normal Distribution function --------------------------------------
// D = Normal distribution (Distribution of the microfacets)
float D_GGX(float NoH, float roughness)
{
	float alpha = pow2(roughness);
	float denom = pow2(NoH) * (pow2(alpha) - 1.0) + 1.0;
	return pow2(alpha / denom);
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
    float ndf = pow2(dot(Dots, Dots)) * alpha.x * alpha.y; // this is really weird
    return 1.0 / ndf;
}
#endif

// Geometric Shadowing visibility function --------------------------------------
	// G = Geometric shadowing term (Microfacets shadowing)
float G_SchlickSmithGGX(float NoL, float NoV, float roughness)
{
    float VisRough = pow2(roughness + 1.0) / 8.0;
    float SchlickVisL = NoL * (1.0 - VisRough) + VisRough;
    float SchlickVisV = NoV * (1.0 - VisRough) + VisRough;
    float Vis = 1.0 / (4.0 * SchlickVisL * SchlickVisV);
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





#if F_RETRO_REFLECTIVE == 1

vec3 GetRetroReflectiveNormal(float retroReflectivity, vec3 L, vec3 V, vec3 N, vec3 H)
{
    vec3 retroReflectionVector = L - reflect(-V, N);
    return normalize(mix(N, retroReflectionVector, retroReflectivity));
}

#endif



#if F_CLOTH_SHADING == 1

// This isn't even based on GGX, lol
float D_Cloth(float roughness, float NoH)
{
    roughness = max(roughness, 1e-6);
    float invRough = 1.0 / roughness;
    return (invRough + 2.0) * pow(1.0 - pow2(NoH), invRough * 0.5);
}

float Vis_Cloth(float NoL, float NoV)
{
    float clampedNoV = min(NoV + 0.001, 1.0);
    return 1.0 / (4.0 * (clampedNoV + NoL - NoL * clampedNoV));
}

vec3 SpecularCloth(float roughness, float NoL, float NoH, float NoV, float VoH, vec3 specularColor)
{
    float NDF = D_Cloth(roughness, NoH);
    float Vis = Vis_Cloth(NoL, NoV);
    vec3 Fresnel = F_Schlick(VoH, specularColor);

    return vec3(NoL * NDF * Vis) * Fresnel;
}
#endif

// Still pass normal due to some effects modifying normal per light
vec3 specularLighting(vec3 lightVector, vec3 normal, MaterialProperties_t mat)
{
	float NoL = saturate( dot(normal, lightVector) );

	vec3 halfVector = normalize(mat.ViewDir + lightVector);

#if (F_RETRO_REFLECTIVE == 1)
    normal = GetRetroReflectiveNormal(mat.ExtraParams.r, lightVector, mat.ViewDir, normal, halfVector);
#endif

	float NoH = saturate( dot(normal, halfVector) );
	float NoV = saturate( dot(normal, mat.ViewDir) );
	float VoH = ClampToPositive(dot(lightVector, halfVector));

#if defined(vr_complex_vfx) && (F_CLOTH_SHADING == 1)
    return SpecularCloth(mat.Roughness.x, NoL, NoH, NoV, VoH, mat.SpecularColor);
#else

#if defined(ANISO_ROUGHNESS)
    // Anisotropic shading
    float NDF = D_AnisoGGX(mat.Roughness, halfVector, normal, mat.AnisotropicTangent, mat.AnisotropicBitangent);
	float Vis = G_SchlickSmithGGX(NoL, NoV, max(mat.Roughness.x, mat.Roughness.y));
#else
    float NDF = D_GGX(NoH, mat.IsometricRoughness);
	float Vis = G_SchlickSmithGGX(NoL, NoV, mat.IsometricRoughness);
#endif
	vec3 F = F_Schlick(VoH, mat.SpecularColor);

#if (F_CLOTH_SHADING == 1)

    float ClothNDF = D_Cloth(mat.IsometricRoughness, NoH);
    float ClothVis = Vis_Cloth(NoL, NoV);

    float blendedClothShading = mix(NDF * Vis, ClothNDF * ClothVis, mat.ClothMask);

    return vec3(blendedClothShading * NoL) * F;
#else
    return vec3(NDF * Vis * NoL) * F;
#endif
#endif
}




// Calculate PBR shading for a light
void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
#if defined(S_DIFFUSE_WRAP)
    vec3 diffuseLight = diffuseWrapped(mat.Normal, lightVector);
#else
    float diffuseLight = diffuseLobe(ClampToPositive(dot(mat.Normal, lightVector)), mat.IsometricRoughness);
#endif
    vec3 specularLight = specularLighting(lightVector, mat.Normal, mat);

    lighting.SpecularDirect += specularLight * lightColor;
    lighting.DiffuseDirect += diffuseLight * lightColor;
}
