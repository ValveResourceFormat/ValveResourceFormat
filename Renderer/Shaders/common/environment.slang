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
#include "object_data.slang"

#include "light_cull.slang"
#include "envmap_data.slang"

#define renderMode_Cubemaps 0
#define renderMode_EnvmapTiles 0

uniform samplerCubeArray g_tEnvironmentMap; // SrgbRead(true) reserved

int g_nEnvMapsIterated = 0;
vec4 g_vEnvMapNormalizationSH = vec4(0.0);

// Where the reflection ray leaves the box, relative to the probe
vec3 CubemapBoxIntersection(vec3 envMapLocalPos, vec3 localReflectionVector, vec3 envMapBoxMin, vec3 envMapBoxMax)
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
    return envMapLocalPos + localReflectionVector * Distance;
}

// Where a reflection ray leaves the sphere, in world space
vec3 CubemapSphereIntersection(vec3 positionWs, vec3 R, vec3 center, float radius)
{
    vec3 fromCenter = positionWs - center;
    float b = dot(fromCenter, R);
    float discriminant = b * b - (dot(fromCenter, fromCenter) - radius * radius);

    if (discriminant <= 0.0)
    {
        return R;
    }

    return normalize(fromCenter + R * (sqrt(discriminant) - b));
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

// Ceiling on brightening only, rising with roughness. A rough reflection is nearly an
// irradiance integral so it tolerates being pushed; a mirror is showing one direction and
// should not be. Darkening is not capped, because that is the occlusion this exists for.

// Specular occlusion: the ratio of the irradiance here to the irradiance the reflection was
// captured with, given as its luminance along the normal.
// With no baked lighting the irradiance falls back to this same cube map at roughness 1, so
// both sides become the same quantity and the ratio settles at 1. Nothing to compare, nothing
// to correct.
float GetEnvMapNormalization(float rough, float bakedLuma, vec3 irradiance)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return 1.0;
    }

    #if (bUseCubemapNormalization == 1)
        if (bakedLuma <= 0.0)
        {
            return 1.0;
        }

        float ceiling = RemapValClamped(rough, CUBEMAP_NORMALIZATION_ROUGHNESS_BEGIN, 1.0, 1.0, CUBEMAP_NORMALIZATION_MAX);
        return min(GetLuma(irradiance) / bakedLuma, ceiling);
    #else
        return 1.0;
    #endif
}


// Lightmapped non-metals switch from the environment maps to the lightmap's own lighting as their
// roughness goes from 0.2 to 0.3, as a bias and scale on roughness.
const vec2 LightmapReflectionRoughnessRange = vec2(-0.2 / (0.3 - 0.2), 1.0 / (0.3 - 0.2));

// The reflection bent toward the normal as the surface gets rougher, where most of what it reflects comes from
vec3 GetDominantReflectionDirection(MaterialProperties_t mat, vec3 reflectionNormal)
{
    vec3 R = reflect(-mat.ViewDir, reflectionNormal);

    float roughnessSquared = pow2(max(mat.Roughness.x, mat.Roughness.y));
    float smoothness = saturate(1.0 - roughnessSquared);

    return mix(reflectionNormal, R, smoothness * (sqrt(smoothness) + roughnessSquared));
}

// BRDF
// Split sum, kept apart so the caller can take the energy the specular claims back off the
// diffuse. The table stores reflectance at F0=0 in x and at F0=1 in y, so a lerp by F0
// recovers the usual scale and bias.
struct EnvBRDF_t
{
    vec3 SingleScatter; // FssEss
    vec3 MultiScatter;  // FmsEms
};

float EnvBRDFCloth(float perceptualRoughness, vec3 N, vec3 V)
{
    float NoV = dot(N, V);
    float NoH = dot(normalize(N + V), N);
    return D_Charlie(pow2(perceptualRoughness), NoH) * V_Neubelt(1.0, NoV) * CLOTH_ENERGY_SCALE;
}

EnvBRDF_t EnvBRDF(vec3 specColor, float perceptualRoughness, vec3 N, vec3 V, float clothMask)
{
    float NdotV = ClampToPositive(dot(N, V));
    vec4 lut = SampleBrdfLut(perceptualRoughness, NdotV, BRDF_LUT_GGX);

    EnvBRDF_t brdf;
    brdf.SingleScatter = mix(vec3(lut.x), vec3(lut.y), specColor);

    if (F_CLOTH_SHADING == 1)
    {
        brdf.SingleScatter = mix(brdf.SingleScatter, specColor * EnvBRDFCloth(perceptualRoughness, N, V), clothMask);
    }

    // What a single bounce misses, fed back through the average Fresnel of a Schlick lobe.
    float energyMissing = 1.0 - lut.y;
    vec3 averageFresnel = specColor + (vec3(1.0) - specColor) * (1.0 / 21.0);

    brdf.MultiScatter = ((brdf.SingleScatter * averageFresnel) / (vec3(1.0) - averageFresnel * energyMissing)) * energyMissing;

    if (F_CLOTH_SHADING == 1)
    {
        brdf.MultiScatter *= 1.0 - clothMask;
    }

    return brdf;
}

// In CS2, anisotropic cubemaps are default enabled with aniso gloss
#if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !anisoCubemapWarpOptional))
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

vec3 GetCorrectedSampleCoords(vec3 R, EnvMapData envData, vec3 envMapLocalPos, float roughness)
{
    #if (ENVMAP_SPHERES)
        // Spheres are parallax corrected in world space, ignoring the probe's rotation
        if (envData.ProjectionType == 0u)
        {
            vec3 sphereIntersection = CubemapSphereIntersection(vFragPosition, R, envData.Origin, envData.BoxMaxs.x);
            return mix(sphereIntersection, R, roughness);
        }
    #endif

    vec3 localReflectionVector = mat4x3(envData.WorldToLocal) * vec4(R, 0.0);

    #if (ENVMAP_PARALLAX_ROUGHNESS_MIX)
        // Parallax corrected against the box, giving way to the plain reflection as the surface gets rougher
        vec3 boxIntersection = CubemapBoxIntersection(envMapLocalPos, localReflectionVector, envData.BoxMins, envData.BoxMaxs);

        #if (ENVMAP_PARALLAX_NORMALIZED)
            boxIntersection = normalize(boxIntersection);
        #endif

        return mix(boxIntersection, localReflectionVector, roughness);
    #else
        return envData.ProjectionType == 1u
            ? normalize(CubemapBoxIntersection(envMapLocalPos, localReflectionVector, envData.BoxMins, envData.BoxMaxs))
            : localReflectionVector;
    #endif
}

void AddEnvMapSample(inout vec3 envMap, inout vec4 normalizationSH, EnvMapData envData, vec3 envMapLocalPos, vec3 R, float roughness, float lod, float weight)
{
    if (g_nEnvMapDebugColors != 0u)
    {
        envMap += envData.Color * weight;
        return;
    }

    vec3 coords = GetCorrectedSampleCoords(R, envData, envMapLocalPos, roughness);
    vec4 envSample = textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod);

    #if (BAKED_LIGHTING_RGBM)
        envMap += (g_vEnvMapSizeConstants.z != 0.0 ? DecodeBakedLightRGBM(envSample) : envSample.rgb) * weight;
    #else
        envMap += envSample.rgb * weight;
    #endif

    normalizationSH += envData.NormalizationSH * weight;
}

// The environment map picked for the whole object
EnvMapData GetObjectEnvMap()
{
    return envMaps[GetObjectLightingData().VisibleLPV >> 16u];
}

// The object's environment map alone, with no blending between environment maps
vec3 SampleObjectEnvMap(vec3 R, vec3 reflectionNormal, float perceptualRoughness, float lod)
{
    EnvMapData envData = GetObjectEnvMap();

    g_nEnvMapsIterated++;
    g_vEnvMapNormalizationSH = envData.NormalizationSH;

    if (g_nEnvMapDebugColors != 0u)
    {
        return envData.Color;
    }

    vec3 envMapLocalPos = mat4x3(envData.WorldToLocal) * vec4(vFragPosition, 1.0);
    vec3 localReflectionVector = mat4x3(envData.WorldToLocal) * vec4(R, 0.0);
    vec3 boxIntersection = CubemapBoxIntersection(envMapLocalPos, localReflectionVector, envData.BoxMins, envData.BoxMaxs);

    // Parallax corrected against the box, giving way to the normal as the surface gets rougher
    vec3 coords = normalize(mix(boxIntersection, reflectionNormal, sqrt(perceptualRoughness)));

    return textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod).rgb;
}

vec3 BlendEnvMaps(vec3 R, float perceptualRoughness, float lod)
{
    vec3 envMap = vec3(0.0);

    // Bit order is priority order, so each environment map only gets what the ones before it left
    // uncovered. Where the sky takes the remainder, the coverage starts empty.
    float flTotalWeight = SKY_FILLS_UNCOVERED ? 0.0 : 0.01;
    bool bContinueLoop = true;
    vec4 normalizationSH = vec4(0.0);

    CullMasks_t masks = GetEnvMapCullMasks(vFragPosition);
    uvec4 objectEnvMaps = GetObjectLightingData().EnvMapVisibility;

    // Otherwise every environment map the tile reaches is taken
    bool bUseObjectEnvMaps = ENVMAP_OBJECT_VISIBILITY;

    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(bContinueLoop); word++)
    {
        uint objectMask = (word < 4u && bUseObjectEnvMaps) ? objectEnvMaps[word] : 0xFFFFFFFFu;

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
            vec3 envMapLocalPos = mat4x3(envData.WorldToLocal) * vec4(vFragPosition, 1.0);

            float weight = 1.0;

            #if (ENVMAP_EDGE_FADE)
                float fade = GetEnvMapEdgeFade(envData, vFragPosition, envMapLocalPos);

                if (fade == 0.0)
                {
                    continue;
                }

                weight = smoothstep(0.0, 1.0, fade) * (1.0 - flTotalWeight);
            #else
                if (any(lessThan(envMapLocalPos, envData.BoxMins)) || any(greaterThan(envMapLocalPos, envData.BoxMaxs)))
                {
                    continue;
                }
            #endif

            flTotalWeight += weight;
            AddEnvMapSample(envMap, normalizationSH, envData, envMapLocalPos, R, perceptualRoughness, lod, weight);

            if (flTotalWeight > 0.99)
            {
                bContinueLoop = false;
            }
        }
    }

    #if (SKY_FILLS_UNCOVERED)
        float skyWeight = 1.0 - flTotalWeight;
        envMap += GetSkyAmbientLighting(R) * skyWeight;
        normalizationSH += GetSkyAmbientLuminanceSH() * skyWeight;
    #elif (ENVMAP_COVERAGE_NORMALIZED)
        envMap /= flTotalWeight;
        normalizationSH /= flTotalWeight;
    #else
        // What no box covers takes the environment map picked for the object, even outside of its box
        float remainingWeight = 1.0 - flTotalWeight;

        if (remainingWeight > 0.0)
        {
            EnvMapData envData = GetObjectEnvMap();
            vec3 envMapLocalPos = mat4x3(envData.WorldToLocal) * vec4(vFragPosition, 1.0);

            g_nEnvMapsIterated++;
            flTotalWeight += remainingWeight;
            AddEnvMapSample(envMap, normalizationSH, envData, envMapLocalPos, R, perceptualRoughness, lod, remainingWeight);
        }

        normalizationSH /= max(flTotalWeight, 1e-4);
    #endif

    g_vEnvMapNormalizationSH = normalizationSH;

    return envMap;
}

vec3 GetEnvironmentNoBRDF(MaterialProperties_t mat, float reflectionCorrectionAmount)
{
    #if (defined(ANISO_ROUGHNESS) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !anisoCubemapWarpOptional))
        vec3 reflectionNormal = CalculateAnisoCubemapWarpVector(mat);
    #else
        vec3 reflectionNormal = mat.AmbientNormal;
    #endif

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        reflectionNormal = mat.GeometricNormal;
        mat.IsometricRoughness = 0.0;
    }

    float perceptualRoughness = saturate(mat.IsometricRoughness);

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, reflectionNormal));

    #if !(ENVMAP_PER_OBJECT)
        // Roughness rather than the perceptual remap, so that a caller raising mat.Roughness to 1
        // for the diffuse lookup lands exactly on the normal.
        float roughnessSquared = pow2(max(mat.Roughness.x, mat.Roughness.y));
        float smoothness = saturate(1.0 - roughnessSquared);
        float dominantDirection = smoothness * (sqrt(smoothness) + roughnessSquared);

        R = normalize(mix(reflectionNormal, R, mix(1.0, dominantDirection, reflectionCorrectionAmount)));
    #endif

    float lod = GetEnvMapLOD(sqrt(perceptualRoughness), R);

    // A scene without environment maps
    if (g_vEnvMapSizeConstants.y == 0.0)
    {
        return g_vUnbakedIndirect.rgb;
    }

    #if (ENVMAP_PER_OBJECT)
        return SampleObjectEnvMap(R, reflectionNormal, perceptualRoughness, lod);
    #else
        return BlendEnvMaps(R, perceptualRoughness, lod);
    #endif
}

vec3 GetEnvironment(MaterialProperties_t mat, float reflectionCorrectionAmount)
{
    vec3 envMap = GetEnvironmentNoBRDF(mat, reflectionCorrectionAmount);

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return envMap;
    }

    vec3 brdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir, mat.ClothMask).SingleScatter;

    return brdf * envMap;
}

vec3 GetEnvironment(MaterialProperties_t mat)
{
    return GetEnvironment(mat, 1.0);
}

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
