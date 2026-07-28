#version 460

#ifndef VRF_ENVIRONMENT_INCLUDE
#define VRF_ENVIRONMENT_INCLUDE

#include "ViewConstants.slang"
#include "features.slang"
#include "utils.slang"
#include "texturing.slang"
#include "LightingConstants.slang"
#include "lighting_common.slang"
#include "pbr.slang"

#include "light_cull.slang"

#define S_SCENE_CUBEMAP_TYPE 0 // 0 = None, 1 = Per-batch cube map, 2 = Per-scene cube map array
#define renderMode_Cubemaps 0
#define renderMode_EnvmapTiles 0

struct EnvMapData
{
    mat4 WorldToLocal;
    vec3 BoxMins;
    uint ArrayIndex;
    vec3 BoxMaxs;
    uint _padding1;
    vec4 InvEdgeWidth;
    vec3 Origin;
    uint ProjectionType;
    vec3 Color;
    uint AssociatedLPV;
    vec4 NormalizationSH;
};

// Maximum number of environment maps supported
#define MAX_ENVMAPS 144

layout(std140, binding = 2) uniform EnvMapArray
{
    EnvMapData envMaps[MAX_ENVMAPS];
};

int g_nEnvMapsIterated = 0;

#if (S_SCENE_CUBEMAP_TYPE == 0)
    // ...
#elif (S_SCENE_CUBEMAP_TYPE == 1)
    uniform samplerCube g_tEnvironmentMap; // SrgbRead(true)

    flat in uint g_nEnvMapIndex;
#elif (S_SCENE_CUBEMAP_TYPE == 2)
    uniform samplerCubeArray g_tEnvironmentMap; // SrgbRead(true)
    flat in uvec4 g_nEnvMapVisibility;
#endif

vec3 CubemapParallaxCorrection(vec3 envMapLocalPos, vec3 localReflectionVector, vec3 envMapBoxMin, vec3 envMapBoxMax)
{
    // https://seblagarde.wordpress.com/2012/09/29/image-based-lighting-approaches-and-parallax-corrected-cubemap/
    // Following is the parallax-correction code
    // Find the ray intersection with box plane
    vec3 FirstPlaneIntersect = (envMapBoxMin - envMapLocalPos) / localReflectionVector;
    vec3 SecondPlaneIntersect = (envMapBoxMax - envMapLocalPos) / localReflectionVector;
    // Get the furthest of these intersections along the ray
    // (Ok because x/0 gives +inf and -x/0 gives -inf )
    vec3 FurthestPlane = max(FirstPlaneIntersect, SecondPlaneIntersect);
    // Find the closest far intersection
    float Distance = abs(min3(FurthestPlane));

    // Get the intersection position
    return normalize(envMapLocalPos + localReflectionVector * Distance);
}


float GetEnvMapLOD(float roughness, vec3 R)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return sin(g_flTime * 3);
    }

    const float EnvMapMipCount = g_vEnvMapSizeConstants.x;
    return roughness * EnvMapMipCount;
}


// Cubemap Normalization
#define bUseCubemapNormalization 1
const vec2 CubemapNormalizationParams = vec2(34.44445, -2.44445); // Normalization value in Alyx. Haven't checked the other games

float GetEnvMapNormalization(float rough, vec3 N, vec3 irradiance)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return 1.0;
    }

    #if (bUseCubemapNormalization == 1)
        float NormalizationTerm = pow(GetLuma(irradiance), 1.5) * 4;

        const float lowestNormalization = 0.1; 
        const float rmin = -0.1;
        const float rmax = 0.9;

        float center = 0.5 * (rmin + rmax);
        float range = 0.5 * (rmax - rmin);
        float dist = saturate(abs(rough - center) / range);
        float smoothdist = smoothstep(lowestNormalization, 1.0, dist);

        return clamp(NormalizationTerm, max(lowestNormalization, dist), 1.0);
    #else
        return 1.0;
    #endif
}


// BRDF
uniform sampler2D g_tBRDFLookup;

vec3 EnvBRDF(vec3 specColor, float perceptualRoughness, vec3 N, vec3 V)
{
    float NdotV = ClampToPositive(dot(N, V));
    vec2 lookupCoords = vec2(NdotV, perceptualRoughness);

    vec2 GGXLut = textureLod(g_tBRDFLookup, lookupCoords, 0.0).xy;
    GGXLut = pow2(GGXLut);

    return specColor * GGXLut.x + GGXLut.y;
}

float EnvBRDFCloth(float perceptualRoughness, vec3 N, vec3 V)
{
    float NoV = dot(N, V);
    float NoH = dot(normalize(N + V), N);
    return D_Charlie(pow2(perceptualRoughness), NoV) * V_Neubelt(NoH, NoH);
}

// In CS2, anisotropic cubemaps are default enabled with aniso gloss
#if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || (GameVfx_vr_complex == 0)))
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

vec3 GetEnvironmentNoBRDF(MaterialProperties_t mat, float reflectionCorrectionAmount)
{
    #if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || (GameVfx_vr_complex == 0)))
        vec3 reflectionNormal = CalculateAnisoCubemapWarpVector(mat);
    #else
        vec3 reflectionNormal = mat.AmbientNormal;
    #endif

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        reflectionNormal = mat.GeometricNormal;
        mat.IsometricRoughness = MIN_ROUGHNESS;
    }

    float perceptualRoughness = RemapValClamped(mat.IsometricRoughness, MIN_ROUGHNESS, 1.0, 0.0, 1.0);
    
    if (F_CLOTH_SHADING == 1)
    {
        perceptualRoughness = mix(perceptualRoughness, sqrt(perceptualRoughness), mat.ClothMask);
    }

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, reflectionNormal));

    // Roughness rather than the perceptual remap, so that a caller raising mat.Roughness to 1
    // for the diffuse lookup lands exactly on the normal.
    float roughnessSquared = pow2(max(mat.Roughness.x, mat.Roughness.y));
    float smoothness = saturate(1.0 - roughnessSquared);
    float dominantDirection = smoothness * (sqrt(smoothness) + roughnessSquared);

    R = normalize(mix(reflectionNormal, R, mix(1.0, dominantDirection, reflectionCorrectionAmount)));

    float lod = GetEnvMapLOD(sqrt(perceptualRoughness), R);

    vec3 envMap = vec3(0.0);

    #if (S_SCENE_CUBEMAP_TYPE == 0)
        envMap = vec3(0.3, 0.3, 0.3);
    #elif (S_SCENE_CUBEMAP_TYPE == 1)
        EnvMapData envData = envMaps[g_nEnvMapIndex];
        bool isBoxProjection = envData.ProjectionType == 1u;
        vec3 envMapBoxMin = envData.BoxMins;
        vec3 envMapBoxMax = envData.BoxMaxs;
        mat4x3 envMapWorldToLocal = mat4x3(envData.WorldToLocal);
        vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

        vec3 coords = GetCorrectedSampleCoords(R, envMapWorldToLocal, envMapLocalPos, isBoxProjection, envMapBoxMin, envMapBoxMax);
        envMap = textureLod(g_tEnvironmentMap, coords, lod).rgb;
    #elif (S_SCENE_CUBEMAP_TYPE == 2)

    float flTotalWeight = 0.01;
    bool bContinueLoop = true;

    CullMasks_t masks = GetEnvMapCullMasks(vFragPosition);
    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(bContinueLoop); word++)
    {
        uint objectMask = word < 4u ? g_nEnvMapVisibility[word] : 0xFFFFFFFFu;

        uint mask = GetCullMaskWord(masks, word, objectMask & GetEnvMapLiveMask(word));

        int baseIndex = int(word * 32u);

        while (mask != 0 && bContinueLoop)
        {
            // Find index of least significant set bit
            int bit = findLSB(mask);

            // Clear the processed bit
            mask &= ~(1u << bit);

            int envMapIndex = baseIndex + bit;

            g_nEnvMapsIterated++;

            EnvMapData envData = envMaps[envMapIndex];
            bool isBoxProjection = envData.ProjectionType == 1u;
            vec3 envMapBoxMin = envData.BoxMins;
            vec3 envMapBoxMax = envData.BoxMaxs;
            mat4x3 envMapWorldToLocal = mat4x3(envData.WorldToLocal);
            vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

            if (any(lessThan(envMapLocalPos, envMapBoxMin)) || any(greaterThan(envMapLocalPos, envMapBoxMax)))
            {
                continue;
            }

            float weight = 1.0f;

            const bool bUseCubemapBlending = S_LIGHTMAP_VERSION_MINOR >= 2;
            if (bUseCubemapBlending && isBoxProjection)
            {
                vec3 envInvEdgeWidth = envData.InvEdgeWidth.xyz;
                vec3 envmapClampedFadeMax = clamp((envMapBoxMax - envMapLocalPos) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
                vec3 envmapClampedFadeMin = clamp((envMapLocalPos - envMapBoxMin) * envInvEdgeWidth, vec3(0.0), vec3(1.0));
                float distanceFromEdge = min(min3(envmapClampedFadeMin), min3(envmapClampedFadeMax));

                if (distanceFromEdge == 0.0)
                {
                    continue;
                }

                // blend using a smooth curve
                weight = (pow2(distanceFromEdge) * (3.0 - (2.0 * distanceFromEdge))) * (1.0 - flTotalWeight);
            }

            flTotalWeight += weight;

            vec3 coords = GetCorrectedSampleCoords(R, envMapWorldToLocal, envMapLocalPos, isBoxProjection, envMapBoxMin, envMapBoxMax);

            envMap += textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod).rgb * weight;

            if (flTotalWeight > 0.99)
            {
                bContinueLoop = false;
            }
        }
    }

    #endif // S_SCENE_CUBEMAP_TYPE == 2

    return envMap;
}

vec3 GetEnvironment(MaterialProperties_t mat, float reflectionCorrectionAmount)
{
    vec3 envMap = GetEnvironmentNoBRDF(mat, reflectionCorrectionAmount);

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return envMap;
    }

    vec3 brdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir);
    if (F_CLOTH_SHADING == 1)
    {
        vec3 clothBrdf = vec3(EnvBRDFCloth(mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir));
        brdf = mix(brdf, clothBrdf, mat.ClothMask);
    }

    return brdf * envMap;
}

vec3 GetEnvironment(MaterialProperties_t mat)
{
    return GetEnvironment(mat, 1.0);
}

// Heat map for renderMode_EnvmapTiles: blue to red as the probe list the tile and depth bin masks hand
// this fragment grows. Counts probes reached, not probes blended, so the containment rejects inside the
// loop show up as heat with no visible reflection change - which is the cost the masks are letting
// through. Returns false where no probe reaches the fragment so the caller leaves it normally shaded.
bool GetEnvMapTileHeatmap(out vec3 color)
{
    const float kBusyTile = 4.0;

    if (g_nEnvMapsIterated == 0)
    {
        color = vec3(0.0);
        return false;
    }

    float t = saturate(float(g_nEnvMapsIterated) / kBusyTile) * 3.0;

    color = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0), saturate(t));
    color = mix(color, vec3(1.0, 1.0, 0.0), saturate(t - 1.0));
    color = mix(color, vec3(1.0, 0.0, 0.0), saturate(t - 2.0));

    return true;
}

#endif
