#version 460
//? #include "features.glsl"
//? #include "utils.glsl"
//? #include "texturing.glsl"
//? #include "LightingConstants.glsl"
//? #include "lighting_common.glsl"
//? #include "pbr.glsl"
//? #include "lighting.glsl"

#define SCENE_ENVIRONMENT_TYPE 0

#if (SCENE_ENVIRONMENT_TYPE == 0) // None or missing environment map
    // ...
#elif (SCENE_ENVIRONMENT_TYPE == 1) // Per-object cube map
    uniform samplerCube g_tEnvironmentMap;
    //uniform int g_iEnvMapArrayIndices[MAX_ENVMAPS];
#elif (SCENE_ENVIRONMENT_TYPE == 2) // Per scene cube map array
    uniform samplerCubeArray g_tEnvironmentMap;
    uniform int g_iEnvMapArrayIndices[MAX_ENVMAPS];
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
    #if (renderMode_Cubemaps == 1)
        return 0.0;
    #endif

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
    #if (bUseCubemapNormalization == 1 && renderMode_Cubemaps == 0)
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
#if ((F_ANISOTROPIC_GLOSS == 1) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !defined(vr_complex_vfx)))
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


vec3 GetEnvironment(MaterialProperties_t mat, out vec3 diffuse)
{
    #if (renderMode_Cubemaps == 1)
        vec3 reflectionNormal = mat.GeometricNormal;
    #elif ((F_ANISOTROPIC_GLOSS == 1) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !defined(vr_complex_vfx)))
        vec3 reflectionNormal = CalculateAnisoCubemapWarpVector(mat);
    #else
        vec3 reflectionNormal = mat.AmbientNormal;
    #endif

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, reflectionNormal));
    vec3 R_Diffuse = mat.AmbientNormal;

    #if (F_ANISOTROPIC_GLOSS == 1)
        float roughness = sqrt(max(mat.Roughness.x, mat.Roughness.y));
    #else
        float roughness = mat.Roughness;
    #endif

    vec3 envMap = vec3(0.0);

    const float lod = GetEnvMapLOD(roughness, R, 0.0);
    const float lod_Diffuse = g_vEnvMapSizeConstants.x; // max lod

    #if (SCENE_ENVIRONMENT_TYPE == 0)
        envMap = max(g_vClearColor.rgb, vec3(0.3, 0.1, 0.1));
        diffuse = max(g_vClearColor.rgb, vec3(0.1, 0.1, 0.3));
    #elif (SCENE_ENVIRONMENT_TYPE == 1)
        envMap = textureLod(g_tEnvironmentMap, R, lod).rgb;
        diffuse = textureLod(g_tEnvironmentMap, R_Diffuse, lod_Diffuse).rgb * 0.5;
    #elif (SCENE_ENVIRONMENT_TYPE == 2)

    float totalWeight = 0.01;

    for (int i = 0; i < 144; i++) {
        int envMapArrayIndex = g_iEnvMapArrayIndices[i];
        vec4 proxySphere = g_vEnvMapProxySphere[envMapArrayIndex];
        bool isBoxProjection = proxySphere.w == 1.0f;
        vec3 envMapBoxMin = g_vEnvMapBoxMins[envMapArrayIndex].xyz - vec3(0.001);
        vec3 envMapBoxMax = g_vEnvMapBoxMaxs[envMapArrayIndex].xyz + vec3(0.001);
        mat4x3 envMapWorldToLocal = mat4x3(g_matEnvMapWorldToLocal[envMapArrayIndex]);
        vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);
        float weight = 1.0f;

        vec3 dists = g_vEnvMapEdgeFadeDists[envMapArrayIndex].xyz;

        if (isBoxProjection) {
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

        vec3 localReflectionVector = envMapWorldToLocal * vec4(R, 0.0);
        vec3 coords = isBoxProjection ? CubemapParallaxCorrection(envMapLocalPos, localReflectionVector, envMapBoxMin, envMapBoxMax) : R;

        #if renderMode_Cubemaps == 0
            // blend to fully corrected
            #if (F_CLOTH_SHADING == 1)
                coords = mix(coords, mat.AmbientNormal, sqrt(roughness));
            #else
                coords = mix(coords, mat.AmbientNormal, roughness);
            #endif
        #endif

        envMap += textureLod(g_tEnvironmentMap, vec4(coords, envMapArrayIndex), lod).rgb * weight;
        diffuse += textureLod(g_tEnvironmentMap, vec4(coords, envMapArrayIndex), lod_Diffuse).rgb * weight;

        if (totalWeight > 0.99)
        {
            break;
        }
    }

    diffuse = mix(diffuse*2.0, diffuse*2.6, GetLuma(diffuse));

    #endif // SCENE_ENVIRONMENT_TYPE == 2

    #if (renderMode_Cubemaps == 1)
        return envMap;
    #endif

    vec3 brdf = EnvBRDF(mat.SpecularColor, GetIsoRoughness(mat.Roughness), mat.AmbientNormal, mat.ViewDir);

    #if (F_CLOTH_SHADING == 1)
        vec3 clothBrdf = vec3(EnvBRDFCloth(GetIsoRoughness(mat.Roughness), mat.AmbientNormal, mat.ViewDir));

        brdf = mix(brdf, clothBrdf, mat.ClothMask);
    #endif

    return brdf * envMap;
}

vec3 GetEnvironment(MaterialProperties_t mat)
{
    vec3 diffuse;
    return GetEnvironment(mat, diffuse);
}
