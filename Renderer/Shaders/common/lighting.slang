#version 460

#ifndef VRF_LIGHTING_INCLUDE
#define VRF_LIGHTING_INCLUDE

#include "features.slang"

#define S_SCENE_PROBE_TYPE 0 // 1 = Individual, 2 = Atlas

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    in vec3 vLightmapUVScaled;
    uniform sampler2DArray g_tIrradiance;
    #if (S_LIGHTMAP_VERSION_MINOR >= 3)
        uniform sampler2DArray g_tDirectionalIrradianceR;
        uniform sampler2DArray g_tDirectionalIrradianceG;
        uniform sampler2DArray g_tDirectionalIrradianceB;
    #else
        uniform sampler2DArray g_tDirectionalIrradiance;
    #endif
    #if (S_LIGHTMAP_VERSION_MINOR == 1)
        uniform sampler2DArray g_tDirectLightIndices;
        uniform sampler2DArray g_tDirectLightStrengths;
    #elif (S_LIGHTMAP_VERSION_MINOR >= 2)
        uniform sampler2DArray g_tDirectLightShadows;
    #endif
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    #include "lighting.lpv.slang"
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    in vec3 vPerVertexLightingOut;
#endif

#include "features.slang"
#include "utils.slang"
#include "texturing.slang"

#include "lighting_common.slang"
#include "shadowmapping.slang"

#include "pbr.slang"
#include "environment.slang"

// https://lisyarus.github.io/blog/graphics/2022/07/30/point-light-attenuation.html
float attenuate_cusp(float s, float falloff)
{
    if (s >= 1.0)
        return 0.0;

    float s2 = pow2(s);
    return pow2(1 - s2) / (1 + falloff * s);
}

void CalculateDirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    const float MIN_ALPHA = 0.0001;

    #if (S_LIGHTMAP_VERSION_MINOR == 1)
        #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
            vec4 dls = texture(g_tDirectLightStrengths, mat.LightmapUv);
            vec4 dli = texture(g_tDirectLightIndices, mat.LightmapUv);
        #elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
            vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
            vec4 dls = textureLod(g_tLPV_Scalars, vLightProbeShadowCoords, 0.0);
            vec4 dli = textureLod(g_tLPV_Indices, vLightProbeShadowCoords, 0.0);
        #else
            vec4 dls = vec4(1, 0, 0, 0);
            vec4 dli = vec4(0, 0, 0, 0);
        #endif

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

            if (IsDirectionalLight(uLightIndex))
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

    #elif (S_LIGHTMAP_VERSION_MINOR >= 2)
        vec4 dlsh = vec4(1, 1, 1, 1);

        #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
            dlsh = textureLod(g_tDirectLightShadows, mat.LightmapUv, 0.0);
        #elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
            vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
            dlsh = textureLod(g_tLPV_Shadows, vLightProbeShadowCoords, 0.0);
        #endif

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
    #else
        // Non lightmapped scene
        const uint uLightIndex = 0;
        vec3 lightColor = GetLightColor(uLightIndex);
        if (length(lightColor) > 0.0)
        {
            float visibility = CalculateSunShadowMapVisibility(mat.PositionWS);
            vec3 lightVector = GetEnvLightDirection(uLightIndex);
            CalculateShading(lighting, lightVector, visibility * lightColor, mat);
        }
    #endif
}


#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)

#define UseLightmapDirectionality true
const float g_flSceneDirectionalLightmapStrength = 0.8;
uniform float g_flDirectionalLightmapStrength = 1.0;
uniform float g_flDirectionalLightmapMinZ = 0.05;

const float colorSpaceMul = 254 / 255;

// I don't actually understand much of this, but it's Valve's code.
vec3 ComputeLightmapShading(vec3 irradianceColor, vec4 irradianceDirection, vec3 normalMap)
{
    if (UseLightmapDirectionality)
    {
        vec3 vTangentSpaceLightVector;

        vTangentSpaceLightVector.xy = UnpackFromColor(irradianceDirection.xy);

        float sinTheta = dot(vTangentSpaceLightVector.xy, vTangentSpaceLightVector.xy);
        float cosTheta = sqrt(1.0 - sinTheta);

        vTangentSpaceLightVector *= (colorSpaceMul / max(colorSpaceMul, length(vTangentSpaceLightVector.xy)));
        vTangentSpaceLightVector.z = cosTheta;

        float flDirectionality = mix(irradianceDirection.z, 1.0, g_flDirectionalLightmapStrength) - g_flSceneDirectionalLightmapStrength;
        vec3 vNonDirectionalLightmap = irradianceColor * saturate(flDirectionality);

        float NoL = ClampToPositive(dot(vTangentSpaceLightVector, normalMap));
        float LightmapZ = max(vTangentSpaceLightVector.z, g_flDirectionalLightmapMinZ);

        irradianceColor = (NoL * (irradianceColor - vNonDirectionalLightmap) / LightmapZ) + vNonDirectionalLightmap;
    }

    return irradianceColor;
}

#endif


void CalculateIndirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    lighting.DiffuseIndirect = vec3(0.3);

    // Indirect Lighting
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vec3 irradiance = texture(g_tIrradiance, mat.LightmapUv).rgb;
    #if (S_LIGHTMAP_VERSION_MINOR >= 3)
        vec4 vAHDData = texture(g_tDirectionalIrradianceR, mat.LightmapUv);
    #else
        vec4 vAHDData = texture(g_tDirectionalIrradiance, mat.LightmapUv);
    #endif

    lighting.DiffuseIndirect = ComputeLightmapShading(irradiance, vAHDData, mat.NormalMap);

    lighting.SpecularOcclusion = vAHDData.a;

#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    lighting.DiffuseIndirect = ComputeLightProbeShading(mat);

#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    lighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
#endif

    // Environment Maps
#if defined(IBL) && (IBL == 1)
    float normalizationTerm = GetEnvMapNormalization(mat.IsometricRoughness, mat.AmbientNormal, lighting.DiffuseIndirect);

    lighting.SpecularIndirect = GetEnvironment(mat) * normalizationTerm;

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0 && D_BAKED_LIGHTING_FROM_PROBE == 0 && D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 0)
        vec2 oldRoughness = mat.Roughness;
        mat.Roughness = vec2(1.0);
        lighting.DiffuseIndirect = GetEnvironmentNoBRDF(mat, 0);
        mat.Roughness = oldRoughness;
    #endif
#endif
}


uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

// AO Proxies would be merged here
void ApplyAmbientOcclusion(inout LightingTerms_t o, MaterialProperties_t mat)
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


LightingTerms_t CalculateLighting(inout MaterialProperties_t mat)
{
    LightingTerms_t lighting = InitLighting();
    mat.Roughness = clamp(mat.Roughness, vec2(0.045), vec2(1.0));

    #if defined(ANISO_ROUGHNESS)
        mat.IsometricRoughness = dot(mat.Roughness, vec2(0.5));
    #else
        mat.IsometricRoughness = mat.Roughness.x;
    #endif

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        mat.LightmapUv = vLightmapUVScaled;
    #endif

    CalculateDirectLighting(lighting, mat);
    CalculateIndirectLighting(lighting, mat);

    return lighting;
}

#define renderMode_Illumination 0
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_Irradiance 0
#define renderMode_Occlusion 0
#define renderMode_LightmapShadows 0

bool _HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
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
    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1 && S_LIGHTMAP_VERSION_MINOR >= 2)
        case renderMode_LightmapShadows:
            vec4 dlsh = texture(g_tDirectLightShadows, mat.LightmapUv);
            outputColor = vec4(vec3(1.0 - dlsh.x) + vec3(1.0 - min3(dlsh.yzw)) * vec3(0.5, 0.5, 0), 1.0);
            return true;
    #endif
        default:
            return false;
    }
}

bool HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
{
    if (_HandleLightingRenderModes(outputColor, mat, lighting))
    {
        outputColor.rgb = SrgbLinearToGamma(outputColor.rgb);
        return true;
    }

    return false;
}

#endif
