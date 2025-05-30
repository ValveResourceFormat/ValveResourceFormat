#version 460
//? #include "features.glsl"
//? #include "utils.glsl"
//? #include "texturing.glsl"
//? #include "LightingConstants.glsl"
//? #include "lighting_common.glsl"
//? #include "pbr.glsl"
//? #include "lighting.glsl"

#define S_SCENE_CUBEMAP_TYPE 0 // 0 = None, 1 = Per-batch cube map, 2 = Per-scene cube map array

#if (S_SCENE_CUBEMAP_TYPE == 0)
    // ...
#elif (S_SCENE_CUBEMAP_TYPE == 1)
    uniform samplerCube g_tEnvironmentMap;
    uniform int g_iEnvMapArrayIndices;
#elif (S_SCENE_CUBEMAP_TYPE == 2)
    uniform samplerCubeArray g_tEnvironmentMap;
    uniform int g_iEnvMapArrayIndices[MAX_ENVMAPS];
    uniform int g_iEnvMapArrayLength;
#endif

vec3 CubemapParallaxCorrection(vec3 envMapLocalPos, vec3 localReflectionVector, vec3 envMapBoxMin, vec3 envMapBoxMax)
{
    // https://seblagarde.wordpress.com/2012/09/29/image-based-lighting-approaches-and-parallax-corrected-cubemap/
    // Following is the parallax-correction code
    // Find the ray intersection with box plane
    vec3 FirstPlaneIntersect = (envMapBoxMin - envMapLocalPos) / localReflectionVector;
    vec3 SecondPlaneIntersect = (envMapBoxMax - envMapLocalPos) / localReflectionVector;
    // Get the furthest of these intersections along the ray
    // (Ok because x/0 give +inf and -x/0 give -inf )
    vec3 FurthestPlane = max(FirstPlaneIntersect, SecondPlaneIntersect);
    // Find the closest far intersection
    float Distance = abs(min3(FurthestPlane));

    // Get the intersection position
    return normalize(envMapLocalPos + localReflectionVector * Distance);
}


float GetEnvMapLOD(float roughness, vec3 R, float clothMask)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return sin(g_flTime * 3);
    }

    const float EnvMapMipCount = g_vEnvMapSizeConstants.x;

    #if F_CLOTH_SHADING == 1
        float lod = mix(roughness, pow(roughness, 0.125), clothMask);
        return lod * EnvMapMipCount;
    #endif

    return roughness * EnvMapMipCount;
}


// Cubemap Normalization
#define bUseCubemapNormalization 0
const vec2 CubemapNormalizationParams = vec2(34.44445, -2.44445); // Normalization value in Alyx. Haven't checked the other games

float GetEnvMapNormalization(float rough, vec3 N, vec3 irradiance)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return 1.0;
    }

    #if (bUseCubemapNormalization == 1)
        // Cancel out lighting
        // SH is currently fully 1.0, for temporary reasons.
        // We don't know how to get the greyscale SH that they use here, because the radiance coefficients in the cubemap texture are rgb.
        float NormalizationTerm = GetLuma(irradiance);// / dot(vec4(N, 1.0), g_vEnvironmentMapNormalizationSH[EnvMapIndex]);

        // Reduce cancellation on glossier surfaces
        float NormalizationClamp = max(1.0, rough * CubemapNormalizationParams.x + CubemapNormalizationParams.y);
        return min(NormalizationTerm, NormalizationClamp);
    #else
        return 1.0;
    #endif
}


// BRDF
uniform sampler2D g_tBRDFLookup;

vec3 EnvBRDF(vec3 specColor, float rough, vec3 N, vec3 V)
{
    float NdotV = ClampToPositive(dot(N, V));
    vec2 lookupCoords = vec2(NdotV, sqrt(rough));

    vec2 GGXLut = textureLod(g_tBRDFLookup, lookupCoords, 0.0).xy;
    GGXLut = pow2(GGXLut);

    return specColor * GGXLut.x + GGXLut.y;
}

#if (F_CLOTH_SHADING == 1)
    float EnvBRDFCloth(float roughness, vec3 N, vec3 V)
    {
        float NoH = dot(normalize(N + V), N);
        return D_Cloth(roughness, NoH) / 8.0;
    }
#endif


// In CS2, anisotropic cubemaps are default enabled with aniso gloss
#if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !defined(vr_complex_vfx)))
    vec3 CalculateAnisoCubemapWarpVector(MaterialProperties_t mat)
    {
        // is this like part of the material struct in the og code? it's calculated at the start
        vec2 roughnessOverRoughness = mat.Roughness.xy / mat.Roughness.yx;
        vec3 warpDirection = mix(mat.AnisotropicBitangent, mat.AnisotropicTangent, vec3(step(roughnessOverRoughness.y, roughnessOverRoughness.x))); // in HLA this just uses vertex tangent

        float warpAmount = (1.0 - min(roughnessOverRoughness.x, roughnessOverRoughness.y)) * 0.5;
        vec3 warpedVector = normalize(cross(cross(mat.ViewDir, warpDirection), warpDirection));

        return normalize(mix(mat.AmbientNormal, warpedVector, warpAmount));
    }
#endif

vec3 GetCorrectedSampleCoords(vec3 R, mat4x3 envMapWorldToLocal, vec3 envMapLocalPos, bool isBoxProjection, vec3 envMapBoxMin, vec3 envMapBoxMax)
{
    vec3 localReflectionVector = envMapWorldToLocal * vec4(R, 0.0);
    return isBoxProjection
        ? CubemapParallaxCorrection(envMapLocalPos, localReflectionVector, envMapBoxMin, envMapBoxMax)
        : localReflectionVector;
}


vec3 GetEnvironment(MaterialProperties_t mat)
{
    #if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !defined(vr_complex_vfx)))
        vec3 reflectionNormal = CalculateAnisoCubemapWarpVector(mat);
    #else
        vec3 reflectionNormal = mat.AmbientNormal;
    #endif

    #if defined(ANISO_ROUGHNESS)
        float roughness = sqrt(max(mat.Roughness.x, mat.Roughness.y));
    #else
        float roughness = mat.Roughness.x;
    #endif

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        reflectionNormal = mat.GeometricNormal;
        roughness = 0.0;
    }

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, reflectionNormal));

    #if (F_CLOTH_SHADING == 1)
        // changed, original was just true
        const bool bIsClothShading = mat.ClothMask > 0.0;
    #else
        const bool bIsClothShading = false;
    #endif

    vec3 envMap = vec3(0.0);

    const float lod = GetEnvMapLOD(roughness, R, 0.0);

    #if (S_SCENE_CUBEMAP_TYPE == 0)
        envMap = vec3(0.3, 0.1, 0.1);
    #elif (S_SCENE_CUBEMAP_TYPE == 1)
        int envMapArrayIndex = g_iEnvMapArrayIndices;
        vec4 proxySphere = g_vEnvMapProxySphere[envMapArrayIndex];
        bool isBoxProjection = proxySphere.w == 1.0f;
        vec3 envMapBoxMin = g_vEnvMapBoxMins[envMapArrayIndex].xyz;
        vec3 envMapBoxMax = g_vEnvMapBoxMaxs[envMapArrayIndex].xyz;
        mat4x3 envMapWorldToLocal = mat4x3(g_matEnvMapWorldToLocal[envMapArrayIndex]);
        vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

        vec3 coords = GetCorrectedSampleCoords(R, envMapWorldToLocal, envMapLocalPos, isBoxProjection, envMapBoxMin, envMapBoxMax);
        coords = mix(coords, mat.AmbientNormal, (bIsClothShading) ? sqrt(roughness) : roughness); // blend to fully corrected

        envMap = textureLod(g_tEnvironmentMap, coords, lod).rgb;
    #elif (S_SCENE_CUBEMAP_TYPE == 2)

    float totalWeight = 0.01;

    for (int i = 0; i < g_iEnvMapArrayLength; i++)
    {
        int envMapArrayIndex = g_iEnvMapArrayIndices[i];
        vec4 proxySphere = g_vEnvMapProxySphere[envMapArrayIndex];
        bool isBoxProjection = proxySphere.w == 1.0f;
        vec3 envMapBoxMin = g_vEnvMapBoxMins[envMapArrayIndex].xyz - vec3(0.01);
        vec3 envMapBoxMax = g_vEnvMapBoxMaxs[envMapArrayIndex].xyz + vec3(0.01);
        mat4x3 envMapWorldToLocal = mat4x3(g_matEnvMapWorldToLocal[envMapArrayIndex]);
        vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);
        float weight = 1.0f;

        const bool bUseCubemapBlending = S_LIGHTMAP_VERSION_MINOR >= 2;
        vec3 dists = g_vEnvMapEdgeFadeDists[envMapArrayIndex].xyz;

        if (bUseCubemapBlending && isBoxProjection)
        {
            vec3 envInvEdgeWidth = 1.0 / dists;
            vec3 envmapClampedFadeMax = clamp((envMapBoxMax - envMapLocalPos) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
            vec3 envmapClampedFadeMin = clamp((envMapLocalPos - envMapBoxMin) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
            float distanceFromEdge = min(min3(envmapClampedFadeMin), min3(envmapClampedFadeMax));

            if (distanceFromEdge == 0.0)
            {
                continue;
            }

            // blend using a smooth curve
            weight = (pow2(distanceFromEdge) * (3.0 - (2.0 * distanceFromEdge))) * (1.0 - totalWeight);
        }

        totalWeight += weight;

        vec3 coords = GetCorrectedSampleCoords(R, envMapWorldToLocal, envMapLocalPos, isBoxProjection, envMapBoxMin, envMapBoxMax);
        coords = mix(coords, mat.AmbientNormal, (bIsClothShading) ? sqrt(roughness) : roughness); // blend to fully corrected

        envMap += textureLod(g_tEnvironmentMap, vec4(coords, envMapArrayIndex), lod).rgb * weight;

        if (totalWeight > 0.99)
        {
            break;
        }
    }

    #endif // S_SCENE_CUBEMAP_TYPE == 2

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return envMap;
    }

    vec3 brdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir);

    #if (F_CLOTH_SHADING == 1)
        vec3 clothBrdf = vec3(EnvBRDFCloth(mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir));

        brdf = mix(brdf, clothBrdf, mat.ClothMask);
    #endif

    return brdf * envMap;
}
