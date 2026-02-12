#language slang 2025
#version 460

import features;
import attributes;



const static bool bakedFromLightmap = D_BAKED_LIGHTING_FROM_LIGHTMAP == 1;
const static bool lightmapVersionIs1 = S_LIGHTMAP_VERSION_MINOR == 1;
const static bool lightmapVersionOver1 = S_LIGHTMAP_VERSION_MINOR > 1;
const static bool lightmapVersionIs2 = S_LIGHTMAP_VERSION_MINOR == 2;
const static bool lightmapVersionOver2 = S_LIGHTMAP_VERSION_MINOR > 2;

public uniform Sampler2DArray g_tIrradiance;
uniform Sampler2DArray g_tIrradianceDebugChar;
uniform Sampler2DArray g_tDirectionalIrradianceR;
uniform Sampler2DArray g_tDirectionalIrradianceG;
uniform Sampler2DArray g_tDirectionalIrradianceB;

uniform Sampler2DArray g_tDirectionalIrradiance;
uniform Sampler2DArray g_tDirectLightIndices;
uniform Sampler2DArray g_tDirectLightStrengths;
uniform Sampler2DArray g_tDirectLightShadows;

//SLANG TODO: 
//#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
//    #include "lighting.lpv.glSlang"
//#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
//    in vec3 vPerVertexLightingOut;
//#endif

static vec3 testScaledUV = globalFragFeatureData.vLightmapUVScaled.get().value;

import lighting_lpv;
import utils;
import texturing;

import lighting_common;
import LightingConstants;
import ViewConstants;

import shadowmapping;

import pbr;
import environment;

public float4 getMeMaLighting()
{
    return float4(g_tIrradiance.Sample(globalFragFeatureData.vLightmapUVScaled.get().value));
}


// https://lisyarus.github.io/blog/graphics/2022/07/30/point-light-attenuation.html
public float attenuate_cusp(float s, float falloff)
{
    if (s >= 1.0)
        return 0.0;

    float s2 = pow2(s);
    return pow2(1 - s2) / (1 + falloff * s);
}

public void CalculateDirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    const float MIN_ALPHA = 0.0001;

    if(S_LIGHTMAP_VERSION_MINOR == 1)
    {
        vec4 dls;
        vec4 dli;
        static if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        {
            dls = g_tDirectLightStrengths.Sample( mat.LightmapUv);
            dli = g_tDirectLightIndices.Sample(mat.LightmapUv);
        }
        else if (D_BAKED_LIGHTING_FROM_PROBE == 1)
        {
            vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
            dls = textureLod(g_tLPV_Scalars, vLightProbeShadowCoords, 0.0);
            dli = textureLod(g_tLPV_Indices, vLightProbeShadowCoords, 0.0);
        }
        else
        {
            dls = vec4(1, 0, 0, 0);
            dli = vec4(0, 0, 0, 0);
        }

        vec4 vLightStrengths = pow2(dls);
        uvec4 vLightIndices = uvec4(dli * 255.0);

        for (int i = 0; i < 4; i++)
        {
            float visibility = vLightStrengths[i];
            if (visibility <= MIN_ALPHA)
            {
                continue;
            }

            uint uLightIndex = vLightIndices[i];
            bool bLightmapBakedDirectDiffuse = true;

            static if (IsDirectionalLight(uLightIndex))
            {
                bLightmapBakedDirectDiffuse = false;
                visibility *= CalculateSunShadowMapVisibility(mat.PositionWS);
            }

            if (visibility > MIN_ALPHA)
            {
                vec3 lightVector = GetLightDirection(mat.PositionWS, uLightIndex);
                vec3 lightColor = GetLightColor(uLightIndex);
                vec3 lightColorModulated = lightColor * visibility;

                vec3 previousDiffuse = lighting.DiffuseDirect;
                CalculateShading(lighting, lightVector, lightColorModulated, mat);

                if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1 && bLightmapBakedDirectDiffuse)
                {
                    lighting.DiffuseDirect = previousDiffuse + lightColorModulated;
                }
            }
        }
    }
    else if (S_LIGHTMAP_VERSION_MINOR >= 2)
    {
        vec4 dlsh = vec4(1, 1, 1, 1);

        if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
            dlsh = g_tDirectLightShadows.SampleLevel(mat.LightmapUv, 0.0);
        else if (D_BAKED_LIGHTING_FROM_PROBE == 1)
        {
            vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
            dlsh = textureLod(g_tLPV_Shadows, vLightProbeShadowCoords, 0.0);
        }

        for(uint uShadowIndex = 0; uShadowIndex < 4; ++uShadowIndex)
        {
            float shadowFactor = 1.0 - dlsh[uShadowIndex];
            if (shadowFactor <= MIN_ALPHA)
            {
                continue;
            }
            uint nLightIndexStart = uShadowIndex == 0 ? 0 : g_nNumLightsPerShadow[uShadowIndex - 1];
            uint nLightCount = g_nNumLightsPerShadow[uShadowIndex];

            for(uint uLightIndex = nLightIndexStart; uLightIndex < nLightCount; ++uLightIndex)
            {
                float visibility = shadowFactor;
                vec3 lightVector = GetLightDirection(mat.PositionWS, uLightIndex);

                if (IsDirectionalLight(uLightIndex))
                {
                    visibility *= CalculateSunShadowMapVisibility(mat.PositionWS);
                }
                else
                {
                    if (!g_bExperimentalLightsEnabled)
                    {
                        continue;
                    }

                    float flInvRange = g_vLightDirection_InvRange[uLightIndex].a * 0.5;
                    vec3 vLightPosition = g_vLightPosition_Type[uLightIndex].xyz;
                    float flDistance = length(vLightPosition - mat.PositionWS);
                    float flFallOff = g_vLightFallOff[uLightIndex].x;

                    // 0.0 near the light, 1.0 at the light maximum range
                    float flDistanceOverRange = flDistance * flInvRange;
                    visibility *= attenuate_cusp(flDistanceOverRange, flFallOff);
                }

                if (visibility > MIN_ALPHA)
                {
                    vec3 lightColor = GetLightColor(uLightIndex);
                    CalculateShading(lighting, lightVector, visibility * lightColor, mat);
                }
            }
        }
    }
    else{
        // Non lightmapped scene
        const uint uLightIndex = 0;
        vec3 lightColor = GetLightColor(uLightIndex);
        if (length(lightColor) > 0.0)
        {
            float visibility = CalculateSunShadowMapVisibility(mat.PositionWS);
            vec3 lightVector = GetEnvLightDirection(uLightIndex);
            CalculateShading(lighting, lightVector, visibility * lightColor, mat);
        }
    }
}



#define UseLightmapDirectionality true
const float g_flSceneDirectionalLightmapStrength = 0.8;
[Default(1)] uniform float g_flDirectionalLightmapStrength = 1.0;
[Default(0.05)] uniform float g_flDirectionalLightmapMinZ = 0.05;

const float colorSpaceMul = 254 / 255;

// I don't actually understand much of this, but it's Valve's code.
public vec3 ComputeLightmapShading(vec3 irradianceColor, vec4 irradianceDirection, vec3 normalMap)
{
    if (UseLightmapDirectionality)
    {
        vec3 vTangentSpaceLightVector;

        vTangentSpaceLightVector.xy = UnpackFromColor(irradianceDirection.xy);

        float sinTheta = dot(vTangentSpaceLightVector.xy, vTangentSpaceLightVector.xy);
        float cosTheta = sqrt(1.0 - sinTheta);

        vTangentSpaceLightVector *= (colorSpaceMul / max(colorSpaceMul, length(vTangentSpaceLightVector.xy)));
        vTangentSpaceLightVector.z = cosTheta;

        float flDirectionality = mix(0, irradianceDirection.z, g_flDirectionalLightmapStrength);

        vec3 vNonDirectionalLightmap = irradianceColor * saturate(flDirectionality);

        float NoL = ClampToPositive(dot(vTangentSpaceLightVector, normalMap));
        float LightmapZ = max(vTangentSpaceLightVector.z, g_flDirectionalLightmapMinZ);

        irradianceColor = (NoL * (irradianceColor - vNonDirectionalLightmap) / LightmapZ) + vNonDirectionalLightmap;
    }

    return irradianceColor;
}

public void CalculateIndirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    lighting.DiffuseIndirect = vec3(0.3);

    // Indirect Lighting
    static if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    {
        vec3 irradiance = g_tIrradiance.Sample(mat.LightmapUv).rgb;
        vec4 vAHDData;
        if (S_LIGHTMAP_VERSION_MINOR >= 3)
            vAHDData = g_tDirectionalIrradianceR.Sample(mat.LightmapUv);
        else
            vAHDData = g_tDirectionalIrradiance.Sample(mat.LightmapUv);

        // vAHDData = vec4(0);
        lighting.DiffuseIndirect = ComputeLightmapShading(irradiance, vAHDData, mat.NormalMap);

        lighting.SpecularOcclusion = vAHDData.a;
    }
    else if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    {
        lighting.DiffuseIndirect = ComputeLightProbeShading(mat);
    }
    else if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    {
        // SLANG TODOlighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
    }

    // Environment Maps
#if defined(IBL) && (IBL == 1)
    float normalizationTerm = GetEnvMapNormalization(mat.IsometricRoughness, mat.AmbientNormal, lighting.DiffuseIndirect);

    lighting.SpecularIndirect = GetEnvironment(mat) * normalizationTerm;

    if(D_BAKED_LIGHTING_FROM_LIGHTMAP == 0 && D_BAKED_LIGHTING_FROM_PROBE == 0 && D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 0)
    {
        vec2 oldRoughness = mat.Roughness;
        mat.Roughness = vec2(1.0);
        lighting.DiffuseIndirect = GetEnvironmentNoBRDF(mat, 0);
        mat.Roughness = oldRoughness;
    }
#endif
}


uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

// AO Proxies would be merged here
public void ApplyAmbientOcclusion(inout LightingTerms_t o, MaterialProperties_t mat)
{
#if defined(DIFFUSE_AO_COLOR_BLEED)
    SetDiffuseColorBleed(mat);
#endif

    // In non-lightmap shaders, SpecularAO always does a min(1.0, specularAO) in the same place where lightmap
    // shaders does min(bakedAO, specularAO). That means that bakedAO exists and is a constant 1.0 in those shaders!
    mat.SpecularAO = min(o.SpecularOcclusion, mat.SpecularAO);

    vec3 DirectAODiffuse = mix(vec3(1.0), mat.DiffuseAO, g_flAmbientOcclusionDirectDiffuse);
    float DirectAOSpecular = mix(1.0, mat.SpecularAO, g_flAmbientOcclusionDirectSpecular);

    o.DiffuseDirect *= DirectAODiffuse;
    o.DiffuseIndirect *= mat.DiffuseAO;
    o.SpecularDirect *= DirectAOSpecular;
    o.SpecularIndirect *= mat.SpecularAO;
}


public LightingTerms_t CalculateLighting(inout MaterialProperties_t mat)
{
    LightingTerms_t lighting = InitLighting();

    if (ANISO_ROUGHNESS != 0)
        mat.IsometricRoughness = dot(mat.Roughness, vec2(0.5));
    else
        mat.IsometricRoughness = max(mat.Roughness.x, mat.Roughness.y);

    if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        mat.LightmapUv = mat.ScaledLightmapUv;

    CalculateDirectLighting(lighting, mat);
    CalculateIndirectLighting(lighting, mat);

    return lighting;
}

#define renderMode_Illumination 0
#define renderMode_Diffuse 1
#define renderMode_Specular 2
#define renderMode_Irradiance 3
#define renderMode_Occlusion 4
#define renderMode_LightmapShadows 5

public bool _HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
{
    switch (g_iRenderMode)
    {
        case renderMode_Illumination:
            outputColor = vec4(lighting.DiffuseDirect + lighting.SpecularDirect, 1.0);
            return true;
        case renderMode_Irradiance:
            outputColor = vec4(lighting.DiffuseIndirect, 1.0);
            return true;
        case renderMode_Diffuse:
            outputColor = vec4((lighting.DiffuseDirect + lighting.DiffuseIndirect) * 0.5, 1.0);
            return true;
        case renderMode_Specular:
            outputColor = vec4((lighting.SpecularDirect + lighting.SpecularIndirect) * 0.5, 1.0);
            return true;
        case renderMode_Occlusion:
            float flLightmapOcclusion = blink() ? 1.0 : lighting.SpecularOcclusion;
            outputColor = vec4(SrgbGammaToLinear((mat.AmbientOcclusion * flLightmapOcclusion).xxx), 1.0);
            return true;
    
        case renderMode_LightmapShadows:
            if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1 && S_LIGHTMAP_VERSION_MINOR >= 2) {
                let directLightIndicesTex = g_tDirectLightIndices;

                vec4 dlsh = directLightIndicesTex.Sample(mat.LightmapUv);
            outputColor = vec4(vec3(1.0 - dlsh.x) + vec3(1.0 - min3(dlsh.yzw)) * vec3(0.5, 0.5, 0), 1.0);
            return true;}
        default:
            return false;
    }
}

public bool HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
{
    if (_HandleLightingRenderModes(outputColor, mat, lighting))
    {
        outputColor.rgb = SrgbLinearToGamma(outputColor.rgb);
        return true;
    }

    return false;
}
