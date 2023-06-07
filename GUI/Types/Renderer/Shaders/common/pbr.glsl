#define PI 3.1415926535897932384626433832795

// Normal Distribution function --------------------------------------
float D_GGX(float NoH, float roughness)
{
	float alpha = roughness * roughness;
	float alpha2 = alpha * alpha;
	float denom = NoH * NoH * (alpha2 - 1.0) + 1.0;
	return (alpha2)/(PI * denom*denom); 
}

// Geometric Shadowing function --------------------------------------
float G_SchlicksmithGGX(float NoL, float NoV, float roughness)
{
	float r = (roughness + 1.0);
	float k = (r * r) / 8.0;
	float GL = NoL / (NoL * (1.0 - k) + k);
	float GV = NoV / (NoV * (1.0 - k) + k);
	return GL * GV;
}

// Fresnel function ----------------------------------------------------
float F_Schlick(float f0, float f90, float VoH) {
    return f0 + (f90 - f0) * pow(1.0 - VoH, 5.0);
}

vec3 F_Schlick(float cosTheta, vec3 F0)
{
	return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}
vec3 F_SchlickR(float cosTheta, vec3 F0, float roughness)
{
	return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(1.0 - cosTheta, 5.0);
}

vec3 specularContribution(vec3 L, vec3 V, vec3 N, vec3 F0, vec3 albedo, float metallic, float roughness)
{
	// Precalculate vectors and dot products	
	vec3 H = normalize (V + L);
	float NoH = clamp(dot(N, H), 0.0, 1.0);
	float NoV = clamp(dot(N, V), 0.0, 1.0);
	float NoL = clamp(dot(N, L), 0.0, 1.0);

	vec3 color = vec3(0.0);

	if (NoL > 0.0) {
		// D = Normal distribution (Distribution of the microfacets)
		float D = D_GGX(NoH, roughness); 
		// G = Geometric shadowing term (Microfacets shadowing)
		float G = G_SchlicksmithGGX(NoL, NoV, roughness);
		// F = Fresnel factor (Reflectance depending on angle of incidence)
		vec3 F = F_Schlick(NoV, F0);
		vec3 spec = D * F * G / (4.0 * NoL * NoV + 0.001);
		vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);			
		color += (kD * albedo / PI + spec) * NoL;
	}

	return color;
}

vec3 diffuseLobe(vec3 diffuse)
{
    return diffuse * (1.0 / PI);
}
