#version 460

#ifndef VRF_ENVMAP_INCLUDE
#define VRF_ENVMAP_INCLUDE

#include "view_constants.slang"
#include "shared_combos.slang"
#include "utils.slang"
#include "texturing.slang"
#include "lighting_constants.slang"
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
    // Find the ray intersection with box plane
    vec3 FirstPlaneIntersect = (envMapBoxMin - envMapLocalPos) / localReflectionVector;
    vec3 SecondPlaneIntersect = (envMapBoxMax - envMapLocalPos) / localReflectionVector;
    // Get the furthest of these intersections along the ray
    // (Ok because x/0 gives +inf and -x/0 gives -inf )
    vec3 FurthestPlane = max(FirstPlaneIntersect, SecondPlaneIntersect);
    // Find the closest far intersection
    float Distance = abs(Min3(FurthestPlane));

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

float GetEnvMapLOD(float roughness)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        return sin(g_flTime * 3);
    }

    float lastMip = g_vEnvMapSizeConstants.x;

    #if (ENVMAP_LOD_SKIPS_SMALLEST_MIP)
        lastMip -= 1.0;
    #endif
    #if (ENV_SPECULAR_SIMPLE)
        lastMip -= 1.0;
    #endif

    return roughness * lastMip;
}

// Cubemap Normalization

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

    if (bakedLuma <= 0.0 || ENV_SPECULAR_SIMPLE == 1)
    {
        return 1.0;
    }

    // Ceiling on brightening only, rising with roughness. A rough reflection is nearly an
    // irradiance integral so it tolerates being pushed; a mirror is showing one direction and
    // should not be. Darkening is not capped, because that is the occlusion this exists for.
    float ceiling = RemapValClamped(rough, CUBEMAP_NORMALIZATION_ROUGHNESS_BEGIN, 1.0, 1.0, CUBEMAP_NORMALIZATION_MAX);
    return min(GetLuma(irradiance) / bakedLuma, ceiling);
}

// Lightmapped non-metals switch from the environment maps to the lightmap's own lighting as their
// roughness goes from 0.2 to 0.3, as a bias and scale on roughness.
const vec2 LightmapReflectionRoughnessRange = vec2(-0.2 / (0.3 - 0.2), 1.0 / (0.3 - 0.2));

// How far the reflection stays toward the mirror direction rather than the normal, from 1 for a mirror to 0
float GetDominantReflectionFactor(float roughnessSquared)
{
    float smoothness = saturate(1.0 - roughnessSquared);

    return smoothness * (sqrt(smoothness) + roughnessSquared);
}

// The reflection bent toward the normal as the surface gets rougher, where most of what it reflects comes from
vec3 GetDominantReflectionDirection(MaterialProperties_t mat, vec3 reflectionNormal)
{
    vec3 R = reflect(-mat.ViewDir, reflectionNormal);

    float roughnessSquared = pow2(max(mat.Roughness.x, mat.Roughness.y));

    return mix(reflectionNormal, R, GetDominantReflectionFactor(roughnessSquared));
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
    return D_Charlie(GetClothRoughness(perceptualRoughness), NoH) * V_Neubelt(1.0, NoV) * CLOTH_ENERGY_SCALE;
}

EnvBRDF_t EnvBRDF(vec3 specColor, float perceptualRoughness, vec3 N, vec3 V, float clothMask)
{
    float NdotV = ClampToPositive(dot(N, V));
    vec4 lut = SampleBrdfLut(perceptualRoughness, NdotV, BRDF_LUT_GGX);

    EnvBRDF_t brdf;
    brdf.SingleScatter = mix(vec3(lut.x), vec3(lut.y), specColor);

    #if (ENV_SPECULAR_SIMPLE)
        brdf.SingleScatter = specColor * lut.y;
        brdf.MultiScatter = vec3(0.0);
        return brdf;
    #endif

    if (F_CLOTH_SHADING == 1)
    {
        #if (CLOTH_VR_LOBE)
            // The vr shaders do not tint cloth reflections by the specular color
            vec3 clothReflectance = vec3(EnvBRDFCloth(perceptualRoughness, N, V));
        #else
            vec3 clothReflectance = specColor * EnvBRDFCloth(perceptualRoughness, N, V);
        #endif

        brdf.SingleScatter = mix(brdf.SingleScatter, clothReflectance, clothMask);
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

#ifndef ANISO_CUBEMAP_WARP_VERTEX_FRAME
    #define ANISO_CUBEMAP_WARP_VERTEX_FRAME 0 // Warp along the vertex tangent frame by the roughness before geometric adjustment
#endif

// In CS2, anisotropic cubemaps are default enabled with aniso gloss
#if (((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1)) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !ANISO_CUBEMAP_WARP_OPTIONAL))
    vec3 CalculateAnisoCubemapWarpVector(MaterialProperties_t mat)
    {
    #if (ANISO_CUBEMAP_WARP_VERTEX_FRAME)
        vec2 roughnessOverRoughness = mat.RoughnessTex.xy / mat.RoughnessTex.yx;
        vec3 warpDirection = mix(mat.Bitangent, mat.Tangent, vec3(step(roughnessOverRoughness.y, roughnessOverRoughness.x)));
    #else
        // is this like part of the material struct in the og code? it's calculated at the start
        vec2 roughnessOverRoughness = mat.Roughness.xy / mat.Roughness.yx;
        vec3 warpDirection = mix(mat.AnisotropicBitangent, mat.AnisotropicTangent, vec3(step(roughnessOverRoughness.y, roughnessOverRoughness.x)));
    #endif

        float warpAmount = (1.0 - min(roughnessOverRoughness.x, roughnessOverRoughness.y)) * 0.5;
        vec3 warpedVector = normalize(cross(cross(mat.ViewDir, warpDirection), warpDirection));

        return normalize(mix(mat.AmbientNormal, warpedVector, warpAmount));
    }
#endif

vec3 GetCorrectedSampleCoords(vec3 R, vec3 reflectionNormal, EnvMapData envData, vec3 envMapLocalPos, float roughness)
{
    #if (ENV_SPECULAR_SIMPLE)
        // The point the reflection hits in the box, not its direction, is what the normal pulls on,
        // so the parallax correction stays until the surface is fully rough
        vec3 simpleLocalReflection = mat4x3(envData.WorldToLocal) * vec4(R, 0.0);
        vec3 simpleHit = simpleLocalReflection;

        #if (F_SPECULAR_CUBE_MAP_PROJECTION == 1)
            if (envData.ProjectionType == 1u)
            {
                simpleHit = CubemapBoxIntersection(envMapLocalPos, simpleLocalReflection, envData.BoxMins, envData.BoxMaxs);
            }
            else
            {
                simpleHit = simpleLocalReflection + envMapLocalPos * envData.InvRadius;
            }
        #endif

        return mix(simpleHit, reflectionNormal, sqrt(roughness));
    #else
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
    #endif
}

void AddEnvMapSample(inout vec3 envMap, inout vec4 normalizationSH, EnvMapData envData, vec3 envMapLocalPos, vec3 R, vec3 reflectionNormal, float roughness, float lod, float weight)
{
    if (g_nEnvMapDebugColors != 0u)
    {
        envMap += envData.Color * weight;
        return;
    }

    vec3 coords = GetCorrectedSampleCoords(R, reflectionNormal, envData, envMapLocalPos, roughness);
    vec4 envSample = textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod);

    #if (BAKED_LIGHTING_RGBM)
        envMap += (g_vEnvMapSizeConstants.z != 0.0 ? DecodeBakedLightRGBM(envSample) : envSample.rgb) * weight;
    #else
        envMap += envSample.rgb * weight;
    #endif

    normalizationSH += envData.NormalizationSH * weight;
}

// The index of the environment map picked for the whole object, when it has none
#define NO_ENV_MAP 0xFFFFu

bool ObjectHasEnvMap()
{
    return (GetObjectLightingData().VisibleLPV >> 16u) != NO_ENV_MAP;
}

// The environment map picked for the whole object
EnvMapData GetObjectEnvMap()
{
    return envMaps[GetObjectLightingData().VisibleLPV >> 16u];
}

// The object's environment map alone, with no blending between environment maps
vec3 SampleObjectEnvMap(vec3 R, vec3 reflectionNormal, float perceptualRoughness, float lod)
{
    if (!ObjectHasEnvMap())
    {
        g_vEnvMapNormalizationSH = vec4(0.0);
        return vec3(0.0);
    }

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

    // The tint of the map scales what it reflects
    return textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod).rgb * envData.Color;
}

vec3 BlendEnvMaps(vec3 R, vec3 reflectionNormal, float perceptualRoughness, float lod)
{
    vec3 envMap = vec3(0.0);

    if (!ObjectHasEnvMap())
    {
        g_vEnvMapNormalizationSH = vec4(0.0);
        return envMap;
    }

    #if (ENV_SPECULAR_SIMPLE)
        // The environment map picked for the object is the only one it reflects
        EnvMapData objectEnvData = GetObjectEnvMap();
        vec3 objectLocalPos = mat4x3(objectEnvData.WorldToLocal) * vec4(vFragPosition, 1.0);
        vec4 objectNormalizationSH = vec4(0.0);

        g_nEnvMapsIterated++;
        AddEnvMapSample(envMap, objectNormalizationSH, objectEnvData, objectLocalPos, R, reflectionNormal, perceptualRoughness, lod, 1.0);
        g_vEnvMapNormalizationSH = objectNormalizationSH;

    #else
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
                AddEnvMapSample(envMap, normalizationSH, envData, envMapLocalPos, R, reflectionNormal, perceptualRoughness, lod, weight);

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
                AddEnvMapSample(envMap, normalizationSH, envData, envMapLocalPos, R, reflectionNormal, perceptualRoughness, lod, remainingWeight);
            }

            normalizationSH /= max(flTotalWeight, 1e-4);
        #endif

        g_vEnvMapNormalizationSH = normalizationSH;
    #endif

    return envMap;
}

vec3 BlendEnvMaps(vec3 R, float perceptualRoughness, float lod)
{
    return BlendEnvMaps(R, R, perceptualRoughness, lod);
}

#if (ENV_SPECULAR_SCHLICK)
// The environment map picked for the object. The reflection is not bent toward the normal by the roughness. Its mip
// is the one that the blur asks for, or the one that matches the texel footprint of the reflection if that is coarser,
// and never coarser than the 4 by 4 mip.
vec3 GetProbeReflection(MaterialProperties_t mat)
{
    if (g_vEnvMapSizeConstants.y == 0.0)
    {
        return vec3(0.3);
    }

    if (!ObjectHasEnvMap())
    {
        g_vEnvMapNormalizationSH = vec4(0.0);
        return vec3(0.0);
    }

    EnvMapData envData = GetObjectEnvMap();

    g_nEnvMapsIterated++;
    g_vEnvMapNormalizationSH = envData.NormalizationSH;

    if (g_nEnvMapDebugColors != 0u)
    {
        return envData.Color;
    }

    vec3 reflectionNormal = mat.AmbientNormal;

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        reflectionNormal = mat.GeometricNormal;
    }

    vec3 R = reflect(-mat.ViewDir, reflectionNormal);

    #if (F_SPECULAR_CUBE_MAP_PROJECTION == 1)
        mat4x3 worldToLocal = mat4x3(envData.WorldToLocal);
        vec3 localPosition = worldToLocal * vec4(mat.PositionWS, 1.0);
        vec3 localReflection = worldToLocal * vec4(R, 0.0);
        vec3 coords = CubemapBoxIntersection(localPosition, localReflection, envData.ProjectionMins, envData.ProjectionMaxs);
    #else
        vec3 coords = R + (mat.PositionWS - envData.Origin) * envData.InvRadius;
    #endif

    float size = float(textureSize(g_tEnvironmentMap, 0).x);
    float log2Size = log2(size);

    vec3 footprintX = dFdxCoarse(R * size * 0.5);
    vec3 footprintY = dFdyCoarse(R * size * 0.5);
    float footprintLod = 0.5 * log2(max(dot(footprintX, footprintX), dot(footprintY, footprintY)));
    float blurLod = log2Size - 10.0 + mat.IsometricRoughness;
    float lod = min(log2Size - 2.0, max(blurLod, footprintLod));

    vec4 envSample = textureLod(g_tEnvironmentMap, vec4(coords, envData.ArrayIndex), lod);

    if (g_vEnvMapSizeConstants.z != 0.0)
    {
        return DecodeBakedLightRGBM(envSample);
    }

    return envSample.rgb;
}
#endif

vec3 GetEnvironmentNoBRDF(MaterialProperties_t mat, float reflectionCorrectionAmount)
{
    #if (ENV_SPECULAR_SCHLICK)
        return GetProbeReflection(mat);
    #endif

    #if (((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1)) && ((F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP == 1) || !ANISO_CUBEMAP_WARP_OPTIONAL))
        vec3 reflectionNormal = CalculateAnisoCubemapWarpVector(mat);
    #else
        vec3 reflectionNormal = mat.AmbientNormal;
    #endif

    float perceptualRoughness = saturate(mat.IsometricRoughness);

    #if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1)) && (VR_SHADING)
        // The vr shaders sample the environment at the rougher of the two directions, the other shaders at their average
        perceptualRoughness = saturate(max(mat.Roughness.x, mat.Roughness.y));
    #endif

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        reflectionNormal = mat.GeometricNormal;
        perceptualRoughness = 0.0;
    }

    // Reflection Vector
    vec3 R = normalize(reflect(-mat.ViewDir, reflectionNormal));

    #if (ENV_SPECULAR_SIMPLE)
        // The lookup is pulled toward the normal after the parallax correction
    #elif !(ENVMAP_PER_OBJECT)
        // Roughness rather than the perceptual remap, so that a caller raising mat.Roughness to 1
        // for the diffuse lookup lands exactly on the normal.
        #if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1)) && (S_LIGHTMAP_VERSION_MINOR >= 2)
            float roughnessSquared = pow2(dot(mat.Roughness, vec2(0.5)));
        #else
            float roughnessSquared = pow2(max(mat.Roughness.x, mat.Roughness.y));
        #endif

        float dominantDirection = GetDominantReflectionFactor(roughnessSquared);

        R = normalize(mix(reflectionNormal, R, mix(1.0, dominantDirection, reflectionCorrectionAmount)));
    #endif

    if (F_RETRO_REFLECTIVE == 1)
    {
        R = normalize(mix(R, mat.ViewDir, mat.RetroReflectivity));
    }

    float lodRoughness = sqrt(perceptualRoughness);

    #if (CLOTH_VR_LOBE)
        if (F_CLOTH_SHADING == 1)
        {
            // Cloth reflections blur sooner as the roughness rises
            lodRoughness = mix(lodRoughness, pow(perceptualRoughness, 0.125), mat.ClothMask);
        }
    #endif

    float lod = GetEnvMapLOD(lodRoughness);

    // A scene without environment maps
    if (g_vEnvMapSizeConstants.y == 0.0)
    {
        return vec3(0.3);
    }

    #if (ENVMAP_PER_OBJECT)
        return SampleObjectEnvMap(R, reflectionNormal, perceptualRoughness, lod);
    #else
        return BlendEnvMaps(R, reflectionNormal, perceptualRoughness, lod);
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

#endif
