// Geometric roughness. Essentially just Specular Anti-Aliasing
#extension GL_ARB_derivative_control : enable // enable DFDX and DFDY

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






//-------------------------------------------------------------------------
//                              NORMALS
//-------------------------------------------------------------------------

// Prevent over-interpolation of vertex normals. Introduced in The Lab renderer
vec3 SwitchCentroidNormal(vec3 vNormalWs, vec3 vCentroidNormalWs)
{
    return ( dot(vNormalWs, vNormalWs) >= 1.01 ) ? vCentroidNormalWs : vNormalWs;
}

vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

vec3 unpackHemiOctNormal(vec4 bumpNormal)
{
    //Reconstruct the tangent vector from the map
#if (HemiOctIsoRoughness_RG_B == 1)
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y - 1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y - 1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif

    // This is free, it gets compiled into the TS->WS matrix mul
    tangentNormal.y *= -1.0;

    return tangentNormal;
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 normalMap, vec3 normal, vec3 tangent, vec3 bitangent)
{
    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * normalMap);
}




//-------------------------------------------------------------------------
//                              ALPHA TEST
//-------------------------------------------------------------------------


#if F_ALPHA_TEST == 1

uniform float g_flAntiAliasedEdgeStrength = 1.0;

float AlphaTestAntiAliasing(float flOpacity, vec2 UVs)
{
	float flAlphaTestAA = saturate( (flOpacity - g_flAlphaTestReference) / ClampToPositive( fwidth(flOpacity) ) + 0.5 );
	float flAlphaTestAA_Amount = min(1.0, length( fwidth(UVs) ) * 4.0);
	float flAntiAliasAlphaBlend = mix(1.0, flAlphaTestAA_Amount, g_flAntiAliasedEdgeStrength);
	return mix( flAlphaTestAA, flOpacity, flAntiAliasAlphaBlend );
}

#endif
