#version 460

#ifndef VRF_LIGHTING_INCLUDE
#define VRF_LIGHTING_INCLUDE

#include "features.slang"

#define S_SCENE_PROBE_TYPE 0 // 1 = Individual, 2 = Atlas

#define renderMode_Illumination 0
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_Irradiance 0
#define renderMode_IrradianceDebug 0
#define renderMode_Occlusion 0
#define renderMode_LightmapShadows 0
#define renderMode_LightTiles 0
#define renderMode_Subgroups 0

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    centroid in vec3 vLightmapUVScaled;
    uniform sampler2DArray g_tIrradiance; // reserved
    uniform sampler2DArray g_tIrradianceDebugChart; // reserved
    #if (S_LIGHTMAP_VERSION_MINOR >= 3)
        uniform sampler2DArray g_tDirectionalIrradianceR; // reserved
        uniform sampler2DArray g_tDirectionalIrradianceG; // reserved
        uniform sampler2DArray g_tDirectionalIrradianceB; // reserved
    #else
        uniform sampler2DArray g_tDirectionalIrradiance; // reserved
    #endif
    #if (S_LIGHTMAP_VERSION_MINOR == 1)
        uniform sampler2DArray g_tDirectLightIndices; // reserved
        uniform sampler2DArray g_tDirectLightStrengths; // reserved
    #elif (S_LIGHTMAP_VERSION_MINOR >= 2)
        uniform sampler2DArray g_tDirectLightShadows; // reserved
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

#include "lighting.barn.slang"

const float MIN_LIGHT_ALPHA = 0.0001;

void CalculateShadingGated(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat, uint nLightIndex)
{
    vec3 previousDiffuse = lighting.DiffuseDirect;
    vec3 previousSpecular = lighting.SpecularDirect;
    vec3 previousTransmissive = lighting.TransmissiveDirect;

    CalculateShading(lighting, lightVector, lightColor, mat);

    vec3 vRenderGates = GetLightRenderGates(nLightIndex);

    // (renderdiffuse / renderspecular / rendertransmissive)
    lighting.DiffuseDirect = mix(previousDiffuse, lighting.DiffuseDirect, vRenderGates.x);
    lighting.SpecularDirect = mix(previousSpecular, lighting.SpecularDirect, vRenderGates.y);
    lighting.TransmissiveDirect = mix(previousTransmissive, lighting.TransmissiveDirect, vRenderGates.z);
}

// The global sun, valid across all lightmap versions. bakedVisibility carries whatever shadowing
// the lightmap already provides (the V2 shadow channel); the sun shadow map goes on top.
void CalculateSunLighting(inout LightingTerms_t lighting, MaterialProperties_t mat, float bakedVisibility)
{
    vec3 vSunColor = GetSunColor();

    if (vSunColor == vec3(0.0))
    {
        return;
    }

    float visibility = bakedVisibility;

    if (visibility > MIN_LIGHT_ALPHA)
    {
        visibility *= CalculateSunShadowMapVisibility(mat.PositionWS);
    }

    if (visibility > MIN_LIGHT_ALPHA)
    {
        vec3 previousSpecular = lighting.SpecularDirect;

        CalculateShading(lighting, GetSunDirection(), vSunColor * visibility, mat);

        if (g_vSunColor.a == 0.0)
        {
            lighting.SpecularDirect = previousSpecular;
        }
    }
}

float CalculateStationaryLightRealtimeShadow(uint nLightIndex, vec3 vPositionWs)
{
    if (float(nLightIndex) == g_vSunLightBakedShadowMask.x)
    {
        return CalculateSunShadowMapVisibility(vPositionWs);
    }

    // todo
    return 1.0;
}

// Per-pixel dynamic light evaluation for legacy lights
void CalculateDynamicLightV1(inout LightingTerms_t lighting, MaterialProperties_t mat, uint nLightIndex)
{
    uint nLightType = GetLightType(nLightIndex);

    vec4 vFalloffParams = g_vLightFallOff[nLightIndex];
    vec3 vLightVector;
    float flDistanceSquared;

    if (nLightType == LIGHT_TYPE_DIRECTIONAL)
    {
        // light_ortho: parallel rays inside a box. Test in beam space and light along the constant beam direction.
        vec3 vDelta = mat.PositionWS - GetLightPositionWs(nLightIndex);
        vec3 vForward = g_vLightDirection_InvRange[nLightIndex].xyz;
        mat3 mLightAxes = mat3(g_matLightToWorld[nLightIndex]);
        vec2 vHalfExtents = g_vLightSpotInnerOuterConeCosines[nLightIndex].xy;

        float flDepth = dot(vDelta, vForward);
        vec2 vSide = vec2(dot(vDelta, mLightAxes[1]), dot(vDelta, mLightAxes[2]));

        if (flDepth < 0.0 || any(greaterThan(abs(vSide), vHalfExtents)))
        {
            return;
        }

        vLightVector = -vForward;
        flDistanceSquared = flDepth * flDepth;
    }
    else
    {
        vec3 vToLight = GetLightPositionWs(nLightIndex) - mat.PositionWS;
        vLightVector = normalize(vToLight);
        flDistanceSquared = dot(vToLight, vToLight);
    }

    if (flDistanceSquared > vFalloffParams.z)
    {
        return;
    }

    float flClampedDistanceSquared = max(flDistanceSquared, 1.0);
    float flAttenuation = saturate(1.0 / dot(vec2(sqrt(flClampedDistanceSquared), flClampedDistanceSquared), vFalloffParams.xy) - vFalloffParams.w);

    if (nLightType == LIGHT_TYPE_SPOT)
    {
        // todo: light cookie

        vec2 vConeCosines = g_vLightSpotInnerOuterConeCosines[nLightIndex].xy;
        vec3 vForward = g_vLightDirection_InvRange[nLightIndex].xyz;
        float flCosAngle = dot(vForward, -vLightVector);
        flAttenuation *= saturate((flCosAngle - vConeCosines.y) / max(vConeCosines.x - vConeCosines.y, 0.0001));
    }

    if (flAttenuation <= MIN_LIGHT_ALPHA)
    {
        return;
    }

    CalculateShadingGated(lighting, vLightVector, GetLightColor(nLightIndex) * flAttenuation, mat, nLightIndex);
}

void CalculateDirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    vec4 dlsh = vec4(1, 1, 1, 1);

    #if (S_LIGHTMAP_VERSION_MINOR == 1)
        #if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
            // Per-vertex baked geometry carries no direct light info.
        #elif (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) || (D_BAKED_LIGHTING_FROM_PROBE == 1)
            #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
                vec4 dls = texture(g_tDirectLightStrengths, mat.LightmapUv);
                vec4 dli = texture(g_tDirectLightIndices, mat.LightmapUv);
            #else
                vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
                vec4 dls = textureLod(g_tLPV_Scalars, vLightProbeShadowCoords, 0.0);
                vec4 dli = textureLod(g_tLPV_Indices, vLightProbeShadowCoords, 0.0);
            #endif

            vec4 vLightStrengths = pow2(dls);
            uvec4 vLightIndices = uvec4(dli * 255.0);

            for (int i = 0; i < 4; i++)
            {
                float visibility = vLightStrengths[i];
                if (visibility <= MIN_LIGHT_ALPHA)
                {
                    continue;
                }

                uint uLightIndex = vLightIndices[i];
                visibility *= CalculateStationaryLightRealtimeShadow(uLightIndex, mat.PositionWS);

                if (visibility > MIN_LIGHT_ALPHA)
                {
                    vec3 lightVector = GetLightDirection(mat.PositionWS, uLightIndex);
                    CalculateShadingGated(lighting, lightVector, GetLightColor(uLightIndex) * visibility, mat, uLightIndex);
                }
            }
        #else
            CalculateSunLighting(lighting, mat, 1.0);
        #endif

        // Dynamic (directlight 2) lights, evaluated per pixel.
        for (uint i = g_nNumLights[0]; i < g_nNumLights[1]; i++)
        {
            CalculateDynamicLightV1(lighting, mat, i);
        }

    #elif (S_LIGHTMAP_VERSION_MINOR >= 2)
        #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
            dlsh = textureLod(g_tDirectLightShadows, mat.LightmapUv, 0.0);
        #elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
            vec3 vLightProbeShadowCoords = CalculateProbeShadowCoords(mat.PositionWS);
            dlsh = textureLod(g_tLPV_Shadows, vLightProbeShadowCoords, 0.0);
        #endif

        CalculateSunLighting(lighting, mat, saturate(1.0 - dot(dlsh, g_vSunLightBakedShadowMask)));
    #else
        // Non lightmapped scene
        CalculateSunLighting(lighting, mat, 1.0);

        for (uint i = g_nNumLights[0]; i < g_nNumLights[1]; i++)
        {
            CalculateDynamicLightV1(lighting, mat, i);
        }
#endif

    // 0 (dynamic scene) or lightmapped 2+ 
    if (S_LIGHTMAP_VERSION_MINOR != 1)
    {
        CalculateBarnLighting(lighting, mat, dlsh);
    }
}


#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)

#define UseLightmapDirectionality true
uniform float g_flDirectionalLightmapStrength = 1.0;
uniform float g_flDirectionalLightmapMinZ = 0.05;

const float LIGHTMAP_DIRECTION_MIN_Z = 0.0872; // sin(5 degrees)
const float LIGHTMAP_DIRECTION_MAX_XY = sqrt(1.0 - LIGHTMAP_DIRECTION_MIN_Z * LIGHTMAP_DIRECTION_MIN_Z);

// I don't actually understand much of this, but it's Valve's code.
vec3 ComputeLightmapShading(vec3 irradianceColor, vec4 irradianceDirection, MaterialProperties_t mat)
{
    if (UseLightmapDirectionality)
    {
        vec2 vTangentSpaceXY = UnpackFromColor(irradianceDirection.xy);

        vTangentSpaceXY *= (LIGHTMAP_DIRECTION_MAX_XY / max(LIGHTMAP_DIRECTION_MAX_XY, length(vTangentSpaceXY)));

        float sinTheta = dot(vTangentSpaceXY, vTangentSpaceXY);
        float cosTheta = sqrt(1.0 - sinTheta);

        float flCurvature = GetCurvature(mat.GeometricNormal, mat.PositionWS);
        float flFlatness = smoothstep(0.1, 0.01, flCurvature) * 0.8;
        float flZScale = mix(1.0, mix(0.1, 2.0, saturate((1.0 - cosTheta) * 1.5)), flFlatness);

        vec3 vTangentSpaceLightVector = normalize(vec3(vTangentSpaceXY, cosTheta * flZScale));

        float flDirectionality = mix(0.0, irradianceDirection.z, g_flDirectionalLightmapStrength);
        vec3 vNonDirectionalLightmap = irradianceColor * saturate(flDirectionality);

        float NoL = ClampToPositive(dot(vTangentSpaceLightVector, mat.NormalMap));
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
    if (g_iRenderMode == renderMode_IrradianceDebug) {
        vec3 irradiance = texture(g_tIrradianceDebugChart, mat.LightmapUv).rgb;
        lighting.DiffuseIndirect = irradiance;
    } else {
        vec3 irradiance = texture(g_tIrradiance, mat.LightmapUv).rgb;
        #if (S_LIGHTMAP_VERSION_MINOR >= 3)
            vec4 vAHDData = texture(g_tDirectionalIrradianceR, mat.LightmapUv);
        #else
            vec4 vAHDData = texture(g_tDirectionalIrradiance, mat.LightmapUv);
        #endif

        lighting.DiffuseIndirect = ComputeLightmapShading(irradiance, vAHDData, mat);

        lighting.SpecularOcclusion = vAHDData.a;
    }

#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    if (g_iRenderMode == renderMode_IrradianceDebug) {
        lighting.DiffuseIndirect = vec3(0);
    } else {
        lighting.DiffuseIndirect = ComputeLightProbeShading(mat);
    }

#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    if (g_iRenderMode == renderMode_IrradianceDebug) {
        lighting.DiffuseIndirect = vec3(1);
    } else {
        lighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
    }
#endif

    // Environment Maps
#if defined(IBL) && (IBL == 1)
    vec3 environment = GetEnvironment(mat);

    lighting.SpecularIndirect = environment
        * GetEnvMapNormalization(mat.IsometricRoughness, mat.AmbientNormal, lighting.DiffuseIndirect);

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0 && D_BAKED_LIGHTING_FROM_PROBE == 0 && D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 0)
        vec2 oldRoughness = mat.Roughness;
        mat.Roughness = vec2(1.0);
        // Roughness is at 1 here, so the full correction lands the lookup on the normal.
        lighting.DiffuseIndirect = GetEnvironmentNoBRDF(mat, 1.0);
        mat.Roughness = oldRoughness;
    #endif

    EnvBRDF_t envBrdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir, mat.ClothMask);

    lighting.SpecularIndirect += envBrdf.MultiScatter * lighting.DiffuseIndirect;

    vec3 diffuseEnergy = ClampToPositive(vec3(1.0) - (envBrdf.SingleScatter + envBrdf.MultiScatter));

    if (F_CLOTH_SHADING == 1)
    {
        diffuseEnergy = mix(diffuseEnergy, vec3(1.0), mat.ClothMask);
    }

    lighting.DiffuseIndirect *= diffuseEnergy;
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
    // shaders do min(bakedAO, specularAO). That means that bakedAO exists and is a constant 1.0 in those shaders!
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

    #if defined(ANISO_ROUGHNESS)
        mat.IsometricRoughness = dot(mat.Roughness, vec2(0.5));
    #else
        mat.IsometricRoughness = max(mat.Roughness.x, mat.Roughness.y);
    #endif

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        mat.LightmapUv = vLightmapUVScaled;
    #endif

    CalculateDirectLighting(lighting, mat);

    lighting.SpecularDirect *= GetSpecularEnergyCompensation(mat);

    CalculateIndirectLighting(lighting, mat);

    return lighting;
}

vec3 GetSubgroupFootprintColor()
{
#ifdef GL_KHR_shader_subgroup_arithmetic
    uvec2 pixel = uvec2(gl_FragCoord.xy);

    uint id = subgroupMin((pixel.y << 16u) | (pixel.x & 0xFFFFu));

    id ^= id >> 16u;
    id *= 0x7feb352du;
    id ^= id >> 15u;
    id *= 0x846ca68bu;
    id ^= id >> 16u;

    vec3 color = vec3(uvec3(id >> 16u, id >> 8u, id) & 0xFFu) / 255.0;

    return mix(color, vec3(1.0), 0.25);
#else
    return vec3(0.0);
#endif
}

bool _HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
{
    switch (g_iRenderMode)
    {
        case renderMode_Illumination:
            outputColor = vec4(lighting.DiffuseDirect + lighting.SpecularDirect, 1.0);
            return true;
        case renderMode_Irradiance:
        case renderMode_IrradianceDebug:
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
        case renderMode_LightTiles:
            vec3 tileHeat;
            outputColor.rgb *= GetLightCullBinStripe(mat.PositionWS);

            if (!GetBarnLightTileHeatmap(tileHeat))
            {
                return true;
            }
            outputColor.rgb = mix(outputColor.rgb, tileHeat, 0.5);
            return true;
        case renderMode_EnvmapTiles:
            vec3 envMapHeat;
            outputColor.rgb *= GetLightCullBinStripe(mat.PositionWS);

            if (!GetEnvMapTileHeatmap(envMapHeat))
            {
                return true;
            }
            outputColor.rgb = mix(outputColor.rgb, envMapHeat, 0.5);
            return true;
        case renderMode_Subgroups:
            outputColor = vec4(GetSubgroupFootprintColor(), 1.0);
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
