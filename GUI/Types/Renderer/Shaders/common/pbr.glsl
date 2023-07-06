#define PI 3.1415926535897932384626433832795

// Normal Distribution function --------------------------------------
float D_GGX(float NoH, float roughness)
{
	float alpha = pow2(roughness);
	float denom = pow2(NoH) * (pow2(alpha) - 1.0) + 1.0;
	return pow2(alpha / denom);
}

// Geometric Shadowing visibility function --------------------------------------
float G_SchlicksmithGGX(float NoL, float NoV, float roughness)
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

vec3 specularContribution(vec3 L, vec3 V, vec3 N, vec3 F0, vec3 specularColor, float roughness)
{
	// Precalculate vectors and dot products	
	float NoL = saturate( dot(N, L) );

	if (NoL > 0.0)
    {
	    vec3 H = normalize(V + L);
	    float NoH = saturate( dot(N, H) );
	    float NoV = saturate( dot(N, V) );
	    float VoH = ClampToPositive(dot(L, H));

        // D = Normal distribution (Distribution of the microfacets)
		float D = D_GGX(NoH, roughness); 
		// G = Geometric shadowing term (Microfacets shadowing)
		float G = G_SchlicksmithGGX(NoL, NoV, roughness);
		// F = Fresnel factor (Reflectance depending on angle of incidence)
		vec3 F = F_Schlick(VoH, F0);

        return vec3(D * G * NoL) * F;
	}

	return vec3(0.0);
}

float diffuseLobe(float NoL, float roughness)
{
    // Pi is never factored into any lighting calculations in Source 2, for some reason (as of HLA)
    float diffuseExponent = (1.0 - roughness) * 0.8 + 0.6;
    return pow(NoL, diffuseExponent) * ((diffuseExponent + 1.0) / 2.0);
    //return diffuse * (1.0 / PI);
}
