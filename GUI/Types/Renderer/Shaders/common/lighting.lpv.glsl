#version 460

#define MAX_LIGHT_PROBES 128

struct LightProbeVolume
{
    mat4x4 WorldToLocalVolumeNormalized;
    vec4 BorderMin;
    vec4 BorderMax;
    vec4 AtlasScale;
    vec4 AtlasOffset;
};

uniform int g_nVisibleLPV;

layout(std140, binding = 3) uniform LightProbeVolumeArray
{
    LightProbeVolume g_LPV[MAX_LIGHT_PROBES];
};

uniform sampler3D g_tLPV_Irradiance;

#if (S_LIGHTMAP_VERSION_MINOR == 1)
    uniform sampler3D g_tLPV_Indices;
    uniform sampler3D g_tLPV_Scalars;
#elif (S_LIGHTMAP_VERSION_MINOR >= 2)
    uniform sampler3D g_tLPV_Shadows;
#endif

#define S_SCENE_PROBE_TYPE 0 // 1 = Individual, 2 = Atlas

vec3 CalculateProbeSampleCoords(in LightProbeVolume lpv, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = mat4x3(lpv.WorldToLocalVolumeNormalized) * vec4(fragPosition, 1.0);
    return vLightProbeLocalPos;
}

vec3 CalculateProbeShadowCoords(LightProbeVolume lpv, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = CalculateProbeSampleCoords(lpv, fragPosition);

    #if (S_SCENE_PROBE_TYPE == 2)
        vLightProbeLocalPos = fma(saturate(vLightProbeLocalPos), lpv.AtlasScale.xyz, lpv.AtlasOffset.xyz);
    #endif

    return vLightProbeLocalPos;
}

vec3 CalculateProbeIndirectCoords(in LightProbeVolume lpv, vec3 fragPosition)
{
    vec3 indirectCoords = CalculateProbeSampleCoords(lpv, fragPosition);

    #if (S_SCENE_PROBE_TYPE == 1)
        indirectCoords.z /= 6;
        // clamp(indirectCoords, lpv.Layer0TextureMin.xyz, lpv.Layer0TextureMax.xyz);
    #elif (S_SCENE_PROBE_TYPE == 2)
        indirectCoords.z /= 6;
        indirectCoords = clamp(indirectCoords, lpv.BorderMin.xyz, lpv.BorderMax.xyz);

        indirectCoords.z *= 6;
        indirectCoords = fma(indirectCoords, lpv.AtlasScale.xyz, lpv.AtlasOffset.xyz);

        indirectCoords.z /= 6;
    #endif

    return indirectCoords;
}

vec3 SampleProbeIndirectDiffuse(in LightProbeVolume lpv, in MaterialProperties_t mat)
{
    vec3 vIndirectSampleCoords = CalculateProbeIndirectCoords(lpv, mat.PositionWS);

    // Take up to 3 samples along the normal direction
    vec3 vDepthSliceOffsets = mix(vec3(0, 1, 2) / 6.0, vec3(3, 4, 5) / 6.0, step(mat.AmbientNormal, vec3(0.0)));
    vec3 vAmbient[3];

    vec3 vNormalSquared = pow2(mat.AmbientNormal);
    vec3 indirectDiffuse = vec3(0.0);

    for (int i = 0; i < 3; i++)
    {
        vAmbient[i] = textureLod(g_tLPV_Irradiance, vIndirectSampleCoords + vec3(0, 0, vDepthSliceOffsets[i]), 0.0).rgb;
        indirectDiffuse += vAmbient[i] * vNormalSquared[i];
    }

    // SteamVR Home lpv irradiance is RGBM Dxt5
    #if (S_LIGHTMAP_VERSION_MINOR == 0)
        indirectDiffuse = pow2(lighting.DiffuseIndirect); // not bothering with RGBM
    #endif

    return indirectDiffuse;
}

vec4 SampleProbeShadows(in LightProbeVolume lpv, in MaterialProperties_t mat)
{
    vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(lpv, mat.PositionWS);
    return textureLod(g_tLPV_Shadows, vLightProbeShadowCoords, 0.0);
}

vec4 GetProbeShadows(in MaterialProperties_t mat)
{
    vec4 shadows = vec4(0);

    float flTotalWeight = 0.01;
    bool bContinueLoop = true;

    for (uint visBucket = 0; visBucket < 4 && bContinueLoop; visBucket++)
    {
        uint mask = g_nEnvMapVisibility[visBucket];
        int baseIndex = int(visBucket * 32);

        while (mask != 0 && bContinueLoop)
        {
            // Find index of least significant set bit
            int bit = findLSB(mask);

            // Clear the processed bit
            mask &= ~(1u << bit);

            int envMapIndex = baseIndex + bit;
            EnvMapData envData = envMaps[envMapIndex];

            if (envData.AssociatedLPV != -1)
            {
                LightProbeVolume lpv = g_LPV[envData.AssociatedLPV];

                vec3 envMapBoxMin = envData.BoxMins;
                vec3 envMapBoxMax = envData.BoxMaxs;
                mat4x3 envMapWorldToLocal = mat4x3(envData.WorldToLocal);
                vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

                vec3 envInvEdgeWidth = envData.InvEdgeWidth.xyz;
                vec3 envmapClampedFadeMax = saturate((envMapBoxMax - envMapLocalPos) * envInvEdgeWidth);
                vec3 envmapClampedFadeMin = saturate((envMapLocalPos - envMapBoxMin) * envInvEdgeWidth);
                float distanceFromEdge = min(min3(envmapClampedFadeMin), min3(envmapClampedFadeMax));

                if (distanceFromEdge == 0.0)
                {
                    continue;
                }

                // blend using a smooth curve
                float weight = (pow2(distanceFromEdge) * (3.0 - (2.0 * distanceFromEdge))) * (1.0 - flTotalWeight);
                shadows += SampleProbeShadows(lpv, mat) * weight;
                flTotalWeight += weight;
            }

            if (flTotalWeight > 0.99)
            {
                bContinueLoop = false;
            }
        }
    }

    //if (blink()) shadows = SampleProbeShadows(g_LPV[g_nVisibleLPV], mat);
    return shadows;
}

vec3 ComputeLightProbeShading(in MaterialProperties_t mat)
{
    vec3 indirectDiffuse = vec3(0.0);
    float flTotalWeight = 0.01;
    bool bContinueLoop = true;

    for (uint visBucket = 0; visBucket < 4 && bContinueLoop; visBucket++)
    {
        uint mask = g_nEnvMapVisibility[visBucket];
        int baseIndex = int(visBucket * 32);

        while (mask != 0 && bContinueLoop)
        {
            // Find index of least significant set bit
            int bit = findLSB(mask);

            // Clear the processed bit
            mask &= ~(1u << bit);

            int envMapIndex = baseIndex + bit;
            EnvMapData envData = envMaps[envMapIndex];

            if (envData.AssociatedLPV != -1)
            {
                LightProbeVolume lpv = g_LPV[envData.AssociatedLPV];
                vec3 envMapBoxMin = envData.BoxMins;
                vec3 envMapBoxMax = envData.BoxMaxs;
                mat4x3 envMapWorldToLocal = mat4x3(envData.WorldToLocal);
                vec3 envMapLocalPos = envMapWorldToLocal * vec4(vFragPosition, 1.0);

                vec3 envInvEdgeWidth = envData.InvEdgeWidth.xyz;
                vec3 envmapClampedFadeMax = saturate((envMapBoxMax - envMapLocalPos) * envInvEdgeWidth);
                vec3 envmapClampedFadeMin = saturate((envMapLocalPos - envMapBoxMin) * envInvEdgeWidth);
                float distanceFromEdge = min(min3(envmapClampedFadeMin), min3(envmapClampedFadeMax));

                if (distanceFromEdge == 0.0)
                {
                    continue;
                }

                // blend using a smooth curve
                float weight = (pow2(distanceFromEdge) * (3.0 - (2.0 * distanceFromEdge))) * (1.0 - flTotalWeight);
                indirectDiffuse += SampleProbeIndirectDiffuse(lpv, mat) * weight;
                flTotalWeight += weight;
            }

            if (flTotalWeight > 0.99)
            {
                bContinueLoop = false;
            }
        }
    }

    //if (blink()) indirectDiffuse = SampleProbeIndirectDiffuse(g_LPV[g_nVisibleLPV], mat);

    return indirectDiffuse;
}
