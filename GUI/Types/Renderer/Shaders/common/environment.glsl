#define SCENE_ENVIRONMENT_TYPE 0

#if (SCENE_ENVIRONMENT_TYPE == 0) // None or missing environment map
    // ...
#else
#if (SCENE_ENVIRONMENT_TYPE == 1) // Per-object cube map
    uniform samplerCube g_tEnvironmentMap;
    uniform vec4 g_vEnvMapBoxMins;
    uniform vec4 g_vEnvMapBoxMaxs;
    uniform mat4x3 g_matEnvMapWorldToLocal;
#elif (SCENE_ENVIRONMENT_TYPE == 2) // Per scene cube map array
    uniform samplerCubeArray g_tEnvironmentMap;
    uniform int g_iEnvMapArrayIndices[MAX_ENVMAPS];
    uniform int g_iEnvironmentMapCount;
#endif

float GetEnvMapLOD(float roughness, vec3 R, vec4 extraParams)
{
    float EnvMapMipCount = g_vEnvMapSizeConstants.x;

    #if F_CLOTH_SHADING == 1
        float lod = mix(roughness, pow(roughness, 0.125), extraParams.b);
        return lod * EnvMapMipCount;
    #else
        return roughness * EnvMapMipCount;
    #endif
}

// Cubemap Normalization
#define bUseCubemapNormalization 0
const vec2 CubemapNormalizationParams = vec2(34.44445, -2.44445); // Normalization value in Alyx. Haven't checked the other games

float GetEnvMapNormalization(float rough, vec3 N, vec3 irradiance)
{
    #if (bUseCubemapNormalization == 1 && renderMode_Cubemaps == 0)
        // Cancel out lighting
        // edit: no cancellation. I don't know how to get the greyscale SH that they use here, because the radiance coefficients in the cubemap texture are rgb.
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
    // done here because of bent normals
    float NdotV = ClampToPositive(dot(N, V));
    vec2 lutCoords = vec2(NdotV, sqrt(rough));

    vec2 GGXLut = pow2(textureLod(g_tBRDFLookup, lutCoords, 0.0).xy);
    return specColor * GGXLut.x + GGXLut.y;
}

// may be different past HLA? looks kinda wrong in cs2. they had a cloth brdf lut iirc
// We could check for the lut and then use this path if it doesn't exist
#if F_CLOTH_SHADING == 1
float EnvBRDFCloth(float roughness, vec3 N, vec3 V)
{
    float NoH = dot(normalize(N + V), N);
    return D_Cloth(roughness, NoH) / 8.0;
}
#endif

#endif

vec3 GetEnvironment(MaterialProperties_t mat, LightingTerms_t lighting)
{
    #if (SCENE_ENVIRONMENT_TYPE == 0)
        return g_vClearColor.rgb;
    #else

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, mat.AmbientNormal));

    float lod = GetEnvMapLOD(mat.Roughness, R, mat.ExtraParams);

    #if (SCENE_ENVIRONMENT_TYPE == 1)
        vec3 coords = R;
        vec3 mins = g_vEnvMapBoxMins.xyz;
        vec3 maxs = g_vEnvMapBoxMaxs.xyz;
        vec3 center = g_matEnvMapWorldToLocal.xyz; // TODO?
        vec3 envMap = textureLod(g_tEnvironmentMap, coords, lod).rgb;
    #elif (SCENE_ENVIRONMENT_TYPE == 2)
        vec3 envMap = vec3(0.0);
        float totalWeight = 0.01;

        for (int i = 0; i < g_vEnvMapSizeConstants.y; i++) {
            int envMapArrayIndex = g_iEnvMapArrayIndices[i];
            vec3 envMapBoxMin = g_vEnvMapBoxMins[envMapArrayIndex].xyz - vec3(0.001);
            vec3 envMapBoxMax = g_vEnvMapBoxMaxs[envMapArrayIndex].xyz + vec3(0.001);
            vec3 dists = g_vEnvMapEdgeFadeDists[envMapArrayIndex].xyz;
            mat4x3 envMapWorldToLocal = mat4x3(g_matEnvMapWorldToLocal[envMapArrayIndex]);
            vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

            vec3 envInvEdgeWidth = 1.0 / dists;
            vec3 envmapClampedFadeMax = clamp((envMapBoxMax - envMapLocalPos) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
            vec3 envmapClampedFadeMin = clamp((envMapLocalPos - envMapBoxMin) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
            float distanceFromEdge = min(min3(envmapClampedFadeMin), min3(envmapClampedFadeMax));

            if (distanceFromEdge == 0.0)
            {
                continue;
            }

            vec3 localReflectionVector = envMapWorldToLocal * vec4(R, 0.0);

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
            vec3 coords = normalize(envMapLocalPos + localReflectionVector * Distance);

            // blend
            float weight = ((distanceFromEdge * distanceFromEdge) * (3.0 - (2.0 * distanceFromEdge))) * (1.0 - totalWeight);
            totalWeight += weight;

            // blend reflection vector from roughness
            #if (F_CLOTH_SHADING == 1)
                coords.xyz = mix(coords.xyz, localReflectionVector, sqrt(mat.Roughness));
            #else
                coords.xyz = mix(coords.xyz, localReflectionVector, mat.Roughness);
            #endif

            envMap += textureLod(g_tEnvironmentMap, vec4(coords, envMapArrayIndex), lod).rgb * weight;

            if (totalWeight > 0.99)
            {
                break;
            }
        }
    #endif

#if (renderMode_Cubemaps == 1)
    return envMap;
#else
    vec3 brdf = EnvBRDF(mat.SpecularColor, mat.Roughness, mat.AmbientNormal, mat.ViewDir);

    #if (F_CLOTH_SHADING == 1)
        vec3 clothBrdf = vec3(EnvBRDFCloth(mat.Roughness, mat.AmbientNormal, mat.ViewDir));

        float clothMask = mat.ExtraParams.z;

        brdf = mix(brdf, clothBrdf, clothMask);
    #endif

    float normalizationTerm = GetEnvMapNormalization(mat.Roughness, mat.AmbientNormal, lighting.DiffuseIndirect);

    return brdf * envMap * normalizationTerm;
#endif
    #endif
}
