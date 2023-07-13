#define PI 3.1415926535897932384626433832795


float diffuseLobe(float NoL, float roughness)
{
    // Pi is never factored into any lighting calculations in Source 2, for some reason (as of HLA)
    float diffuseExponent = (1.0 - roughness) * 0.8 + 0.6;
    return pow(NoL, diffuseExponent) * ((diffuseExponent + 1.0) / 2.0);
    //return diffuse * (1.0 / PI);
}



// Normal Distribution function --------------------------------------
float D_GGX(float NoH, float roughness)
{
	float alpha = pow2(roughness);
	float denom = pow2(NoH) * (pow2(alpha) - 1.0) + 1.0;
	return pow2(alpha / denom);
}


// Geometric Shadowing visibility function --------------------------------------
float G_SchlickSmithGGX(float NoL, float NoV, float roughness)
{
    float VisRough = pow2(roughness + 1.0) / 8.0;
    float SchlickVisL = NoL * (1.0 - VisRough) + VisRough;
    float SchlickVisV = NoV * (1.0 - VisRough) + VisRough;
    float Vis = 1.0 / (4.0 * SchlickVisL * SchlickVisV);
    return Vis;
}

// Fresnel function ----------------------------------------------------
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
    return normalize(mix(H, retroReflectionVector, retroReflectivity));
}

#endif



#if F_CLOTH_SHADING == 1

// This isn't even based on GGX, lol
float D_Cloth(float roughness, float NoH)
{
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


vec3 specularLighting(vec3 L, vec3 V, vec3 N, vec3 F0, vec3 specularColor, float roughness, vec4 extraParams)
{
	float NoL = saturate( dot(N, L) );

	vec3 H = normalize(V + L);

#if F_RETRO_REFLECTIVE == 1
    H = GetRetroReflectiveNormal(extraParams.r, L, V, N, H);
#endif

	float NoH = saturate( dot(N, H) );
	float NoV = saturate( dot(N, V) );
	float VoH = ClampToPositive(dot(L, H));

#if defined(vr_complex) && (F_CLOTH_SHADING == 1)
    return SpecularCloth(roughness, NoL, NoH, NoV, VoH, specularColor);
#endif
    // D = Normal distribution (Distribution of the microfacets)
	float NDF = D_GGX(NoH, roughness); 
	// G = Geometric shadowing term (Microfacets shadowing)
	float Vis = G_SchlickSmithGGX(NoL, NoV, roughness);
	// F = Fresnel factor (Reflectance depending on angle of incidence)
	vec3 F = F_Schlick(VoH, F0);

#if (F_CLOTH_SHADING == 1)
    // I'm not sure how they blend to the cloth shading, but I'm assuming it's just blending shading
    float ClothNDF = D_Cloth(roughness, NoH);
    float ClothVis = Vis_Cloth(NoL, NoV);

    float clothMask = extraParams.z;

    float blendedClothShading = mix(NDF * Vis, ClothNDF * ClothVis, clothMask);

    return vec3(blendedClothShading * NoL) * F;
#else
    return vec3(NDF * Vis * NoL) * F;
#endif
}
