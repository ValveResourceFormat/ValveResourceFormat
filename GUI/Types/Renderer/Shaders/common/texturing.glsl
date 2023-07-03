#extension GL_ARB_derivative_control : enable

float CalculateGeometricRoughnessFactor(vec3 geometricNormal)
{
	vec3 normalDerivX = dFdxCoarse(geometricNormal);
	vec3 normalDerivY = dFdyCoarse(geometricNormal);
	float geometricRoughnessFactor = pow(saturate(max(dot(normalDerivX, normalDerivX), dot(normalDerivY, normalDerivY))), 0.333);
	return geometricRoughnessFactor;
}

float AdjustRoughnessByGeometricNormal( float roughness, vec3 geometricNormal )
{
	float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(geometricNormal);

	return max(roughness, geometricRoughnessFactor);
}
