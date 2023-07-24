// Geometric roughness. Essentially just Specular Anti-Aliasing
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







float applyBlendModulation(float blendFactor, float blendMask, float blendSoftness)
{
    float minb = max(0.0, blendMask - blendSoftness);
    float maxb = min(1.0, blendMask + blendSoftness);

    return smoothstep(minb, maxb, blendFactor);
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








//-------------------------------------------------------------------------
//                              DETAIL TEXTURING
//-------------------------------------------------------------------------

#if (F_DETAIL_TEXTURE != 0)
uniform float g_flDetailBlendFactor = 1.0;
uniform float g_flDetailBlendToFull = 0.0;
uniform float g_flDetailNormalStrength = 1.0;
in vec2 vDetailTexCoords;

uniform sampler2D g_tDetailMask;
#if (F_DETAIL_TEXTURE > 0) && (F_DETAIL_TEXTURE != 3)
uniform sampler2D g_tDetail;
#endif
#if (F_DETAIL_TEXTURE == 3) || (F_DETAIL_TEXTURE == 4)
uniform sampler2D g_tNormalDetail;
#endif
in vec2 vDetailTexoords;

#define MOD2X_MUL 1.9922
#define DETAIL_CONST 0.9961


void applyDetailTexture(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords)
{
    float detailMask = texture(g_tDetailMask, detailMaskCoords).x;
    detailMask = g_flDetailBlendFactor * max(detailMask, g_flDetailBlendToFull);

    // MOD2X
#if F_DETAIL_TEXTURE == 1

    vec3 DetailTexture = texture(g_tDetail, vDetailTexCoords).rgb * MOD2X_MUL;
    Albedo *= mix(vec3(1.0), DetailTexture, detailMask);

// OVERLAY
#elif (F_DETAIL_TEXTURE == 2) || (F_DETAIL_TEXTURE == 4)

    vec3 DetailTexture = DETAIL_CONST * texture(g_tDetail, vDetailTexCoords).rgb;

    // blend in linear space!!!
    vec3 linearAlbedo = pow(Albedo, vec3(1.0 / 2.2));
    vec3 overlayScreen = 1.0 - (1.0 - DetailTexture) * (1.0 - linearAlbedo) * 2.0;
    vec3 overlayMul = DetailTexture * linearAlbedo * 2.0;

    vec3 linearBlendedOverlay = mix(overlayMul, overlayScreen, greaterThanEqual(linearAlbedo, vec3(0.5)));
    vec3 gammaBlendedOverlay = pow(linearBlendedOverlay, vec3(2.2));

    Albedo = mix(Albedo, gammaBlendedOverlay, detailMask);

#endif


// NORMALS
#if (F_DETAIL_TEXTURE == 3) || (F_DETAIL_TEXTURE == 4)
    vec3 DetailNormal = unpackHemiOctNormal(texture(g_tNormalDetail, vDetailTexCoords));
    DetailNormal = mix(vec3(0, 0, 1), DetailNormal, detailMask * g_flDetailNormalStrength);
    // literally i dont even know
    NormalMap = NormalMap * DetailNormal.z + vec3(NormalMap.z * DetailNormal.z * DetailNormal.xy, 0.0);

#endif
}

#endif
