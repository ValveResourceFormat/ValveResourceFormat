#version 460

#ifndef VRF_LIGHTING_LPV_INCLUDE
#define VRF_LIGHTING_LPV_INCLUDE

#include "features.slang"
#include "utils.slang"
#include "material_properties.slang"
#include "light_cull.slang"
#include "object_data.slang"
#include "LightingConstants.slang"
#include "envmap_data.slang"

#define MAX_LIGHT_PROBES 320

struct LightProbeVolume
{
    mat4x4 WorldToLocalVolumeNormalized;
    vec4 BorderMin;
    vec4 BorderMax;
    vec4 AtlasScale;
    vec4 AtlasOffset;
    vec4 InvEdgeWidth;
    vec4 BoundsExtend;
};

layout(std140, binding = 3) uniform LightProbeVolumeArray
{
    LightProbeVolume g_LPV[MAX_LIGHT_PROBES];
};

uniform sampler3D g_tLPV_Irradiance; // SrgbRead(true) reserved

#if (BAKED_LIGHT_INDICES)
    uniform sampler3D g_tLPV_Indices; // reserved
    uniform sampler3D g_tLPV_Scalars; // reserved
#elif (BAKED_LIGHT_SHADOW_MASK)
    uniform sampler3D g_tLPV_Shadows; // reserved
#endif

vec3 CalculateProbeSampleCoords(uint nVolume, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = mat4x3(g_LPV[nVolume].WorldToLocalVolumeNormalized) * vec4(fragPosition, 1.0);
    return vLightProbeLocalPos;
}

vec3 CalculateProbeShadowCoords(LightProbeVolume lpv, vec3 vLightProbeLocalPos)
{
    #if (BAKED_LIGHT_SHADOW_MASK)
        vLightProbeLocalPos = saturate(vLightProbeLocalPos);
    #else
        // Kept half a probe inside the volume, as the irradiance is, so filtering never reaches the next
        // volume in the atlas. The borders are in units of one irradiance face, a sixth of the depth.
        vec3 vFaceDepth = vec3(1.0, 1.0, 6.0);
        vLightProbeLocalPos = clamp(vLightProbeLocalPos, lpv.BorderMin.xyz * vFaceDepth, lpv.BorderMax.xyz * vFaceDepth);
    #endif

    return fma(vLightProbeLocalPos, lpv.AtlasScale.xyz, lpv.AtlasOffset.xyz);
}

vec3 CalculateProbeShadowCoords(vec3 fragPosition)
{
    uint nVolume = GetObjectLightingData().VisibleLPV & 0xFFFFu;
    return CalculateProbeShadowCoords(g_LPV[nVolume], CalculateProbeSampleCoords(nVolume, fragPosition));
}

vec3 CalculateProbeIndirectCoords(LightProbeVolume lpv, vec3 indirectCoords)
{
    indirectCoords.z /= 6;
    indirectCoords = clamp(indirectCoords, lpv.BorderMin.xyz, lpv.BorderMax.xyz);

    indirectCoords.z *= 6;
    indirectCoords = fma(indirectCoords, lpv.AtlasScale.xyz, lpv.AtlasOffset.xyz);

    indirectCoords.z /= 6;

    return indirectCoords;
}

vec3 DecodeProbeSample(vec4 value)
{
    #if (BAKED_LIGHTING_RGBM)
        return DecodeBakedLightRGBM(value);
    #endif

    return value.rgb;
}

vec3 SampleProbeAmbientCube(vec3 vIndirectSampleCoords, vec3 normal)
{
    // Take up to 3 samples along the normal direction
    vec3 vDepthSliceOffsets = mix(vec3(0, 1, 2) / 6.0, vec3(3, 4, 5) / 6.0, step(normal, vec3(0.0)));
    vec3 vAmbient[3];

    vec3 vNormalSquared = pow2(normal);

    vec3 indirectDiffuse = vec3(0.0);

    for (int i = 0; i < 3; i++)
    {
        vAmbient[i] = DecodeProbeSample(textureLod(g_tLPV_Irradiance, vIndirectSampleCoords + vec3(0, 0, vDepthSliceOffsets[i]), 0.0));
        indirectDiffuse += vAmbient[i] * vNormalSquared[i];
    }

    return indirectDiffuse;
}

// What the volumes reaching a position bake for it, blended
struct LightProbeSample_t
{
    vec3 Irradiance;
    vec4 Shadows;
    float Coverage;
};

void AddLightProbeVolume(inout LightProbeSample_t probe, LightProbeVolume lpv, vec3 localCoords, vec3 normal, float weight)
{
    probe.Irradiance += SampleProbeAmbientCube(CalculateProbeIndirectCoords(lpv, localCoords), normal) * weight;

    #if (BAKED_LIGHT_SHADOW_MASK)
        probe.Shadows += textureLod(g_tLPV_Shadows, CalculateProbeShadowCoords(lpv, localCoords), 0.0) * weight;
    #endif

    probe.Coverage += weight;
}

#if (PROBES_FROM_ENVMAPS)

// Volumes are reached through the environment maps sharing their handshake, faded and ordered as those are
void BlendLightProbeVolumes(inout LightProbeSample_t probe, vec3 positionWs, vec3 normal)
{
    float envMapCoverage = 0.0;
    bool bContinueLoop = true;

    CullMasks_t masks = GetEnvMapCullMasks(positionWs);

    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(bContinueLoop); word++)
    {
        uint mask = GetCullMaskWord(masks, word, GetEnvMapLiveMask(word));

        while (mask != 0u && bContinueLoop)
        {
            int bit = findLSB(mask);
            mask &= ~(1u << bit);

            EnvMapData envData = envMaps[(word * 32u) + uint(bit)];
            vec3 envMapLocalPos = mat4x3(envData.WorldToLocal) * vec4(positionWs, 1.0);
            float fade = GetEnvMapEdgeFade(envData, positionWs, envMapLocalPos);

            if (fade == 0.0)
            {
                continue;
            }

            float blend = smoothstep(0.0, 1.0, fade);
            envMapCoverage += blend * (1.0 - envMapCoverage);

            if (envData.AssociatedLPV >= 0)
            {
                uint nVolume = uint(envData.AssociatedLPV);
                vec3 localCoords = CalculateProbeSampleCoords(nVolume, positionWs);
                AddLightProbeVolume(probe, g_LPV[nVolume], localCoords, normal, blend * (1.0 - probe.Coverage));
            }

            bContinueLoop = envMapCoverage <= 0.99 || probe.Coverage <= 0.99;
        }
    }
}

#else

// Bit order is priority order, so each volume only gets what the ones before it left uncovered
void BlendLightProbeVolumes(inout LightProbeSample_t probe, vec3 positionWs, vec3 normal)
{
    bool bContinueLoop = true;

    CullMasks_t masks = GetLightProbeCullMasks(positionWs);

    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(bContinueLoop); word++)
    {
        uint mask = GetCullMaskWord(masks, word, GetLightProbeLiveMask(word));

        while (mask != 0u && bContinueLoop)
        {
            int bit = findLSB(mask);
            mask &= ~(1u << bit);

            uint nVolume = (word * 32u) + uint(bit);
            LightProbeVolume lpv = g_LPV[nVolume];
            vec3 localCoords = CalculateProbeSampleCoords(nVolume, positionWs);

            vec3 vFadeFromMin = saturate((localCoords + lpv.BoundsExtend.xyz) * lpv.InvEdgeWidth.xyz);
            vec3 vFadeFromMax = saturate((1.0 - localCoords + lpv.BoundsExtend.xyz) * lpv.InvEdgeWidth.xyz);
            float flWeight = smoothstep(0.0, 1.0, min(min3(vFadeFromMin), min3(vFadeFromMax))) * (1.0 - probe.Coverage);

            if (flWeight > 0.0)
            {
                AddLightProbeVolume(probe, lpv, localCoords, normal, flWeight);
            }

            bContinueLoop = probe.Coverage <= 0.99;
        }
    }
}

#endif

LightProbeSample_t SampleLightProbeVolumes(vec3 positionWs, vec3 normal)
{
    LightProbeSample_t probe = LightProbeSample_t(vec3(0.0), vec4(0.0), 0.0);

    #if defined(ENABLE_LPV_BLENDING) || (D_LPV_BLENDING == 1) || (PROBE_BLENDING)
        BlendLightProbeVolumes(probe, positionWs, normal);
    #else
        uint nVolume = GetObjectLightingData().VisibleLPV & 0xFFFFu;
        AddLightProbeVolume(probe, g_LPV[nVolume], CalculateProbeSampleCoords(nVolume, positionWs), normal, 1.0);
    #endif

    return probe;
}

// The shadows and the irradiance are blended from the same volumes, so one pass serves both
LightProbeSample_t g_LightProbeSample = LightProbeSample_t(vec3(0.0), vec4(0.0), 0.0);
vec3 g_vLightProbeSampleNormal = vec3(0.0);
bool g_bLightProbeSampleReady = false;

LightProbeSample_t GetLightProbeSample(vec3 positionWs, vec3 normal)
{
    if (!g_bLightProbeSampleReady || g_vLightProbeSampleNormal != normal)
    {
        g_LightProbeSample = SampleLightProbeVolumes(positionWs, normal);
        g_vLightProbeSampleNormal = normal;
        g_bLightProbeSampleReady = true;
    }

    return g_LightProbeSample;
}

// Zero without a baked shadow mask, which reads as unshadowed
vec4 SampleProbeDirectLightShadows(vec3 positionWs, vec3 normal)
{
    #if (BAKED_LIGHT_SHADOW_MASK)
        return GetLightProbeSample(positionWs, normal).Shadows;
    #else
        return vec4(0.0);
    #endif
}

vec4 SampleProbeDirectLightShadows(vec3 positionWs)
{
    return SampleProbeDirectLightShadows(positionWs, g_bLightProbeSampleReady ? g_vLightProbeSampleNormal : vec3(0.0, 0.0, 1.0));
}

vec3 SampleLightProbeIrradiance(vec3 positionWs, vec3 normal)
{
    LightProbeSample_t probe = GetLightProbeSample(positionWs, normal);

    // Sky from above and its ground bounce from below, for what no volume covers
    float skyWeight = 1.0 - probe.Coverage;

    #if !(SKY_FILLS_UNCOVERED)
        skyWeight = probe.Coverage < 0.99 ? skyWeight : 0.0;
    #endif

    return probe.Irradiance + GetSkyAmbientLighting(normal) * skyWeight;
}

vec3 ComputeLightProbeShading(in MaterialProperties_t mat)
{
    return SampleLightProbeIrradiance(mat.PositionWS, mat.AmbientNormal);
}

#endif
