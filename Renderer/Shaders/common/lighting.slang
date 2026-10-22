#version 460

#ifndef VRF_LIGHTING_INCLUDE
#define VRF_LIGHTING_INCLUDE

#include "features.slang"

#include "light_cull.slang"
#include "object_data.slang"
#include "envmap_data.slang"

#define renderMode_Illumination 0
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_Irradiance 0
#define renderMode_LightmapCharts 0
#define renderMode_Occlusion 0
#define renderMode_LightmapShadows 0
#define renderMode_LightTiles 0
#define renderMode_Subgroups 0

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    centroid in vec3 vLightmapUVScaled;
    uniform sampler2DArray g_tIrradiance; // reserved
    uniform sampler2DArray g_tLightmapDebugCharts; // reserved
    #if (LIGHTMAP_SH)
        uniform sampler2DArray g_tDirectionalIrradianceR; // reserved
        uniform sampler2DArray g_tDirectionalIrradianceG; // reserved
        uniform sampler2DArray g_tDirectionalIrradianceB; // reserved
    #else
        uniform sampler2DArray g_tDirectionalIrradiance; // reserved
    #endif
    #if (BAKED_LIGHT_INDICES)
        uniform sampler2DArray g_tDirectLightIndices; // reserved
        uniform sampler2DArray g_tDirectLightStrengths; // reserved
    #elif (BAKED_LIGHT_SHADOW_MASK)
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

        ApplyMinRoughness(mat, g_vSunDirection.w);

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
        flDistanceSquared = length_squared(vToLight);
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
    // Unshadowed where nothing baked the shadows, such as per vertex lighting
    vec4 dlsh = vec4(0.0);

    #if (BAKED_LIGHT_INDICES)
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

    #elif (BAKED_LIGHT_SHADOW_MASK)
        #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
            dlsh = textureLod(g_tDirectLightShadows, mat.LightmapUv, 0.0);
        #elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
            dlsh = SampleProbeDirectLightShadows(mat.PositionWS, mat.AmbientNormal);
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

    // Scenes with baked light indices light through those and the dynamic lights above instead
    #if !(BAKED_LIGHT_INDICES)
        CalculateBarnLighting(lighting, mat, dlsh);
    #endif
}


#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)

const float LIGHTMAP_DIRECTION_MIN_Z = 0.0872; // sin(5 degrees)
const float LIGHTMAP_DIRECTION_MAX_XY = sqrt(1.0 - LIGHTMAP_DIRECTION_MIN_Z * LIGHTMAP_DIRECTION_MIN_Z);

// The direction texture holds the dominant incoming direction in tangent space in xy, and in z the
// share of the irradiance that arrives from all around rather than from that direction. The directional
// share is divided by the direction's z, so a direction close to the surface brightens it the most.
vec3 ShadeDirectionalLightmap(vec3 irradianceColor, vec3 nonDirectional, vec3 lightVectorTs, float lightVectorZ, vec3 normalTs)
{
    float NoL = ClampToPositive(dot(lightVectorTs, normalTs));
    return (NoL * (irradianceColor - nonDirectional) / lightVectorZ) + nonDirectional;
}

#if (LIGHTMAP_DIRECTION_CLAMP)

// The direction is kept 5 degrees off the surface
vec3 ComputeLightmapShading(vec3 irradianceColor, vec3 irradianceDirection, MaterialProperties_t mat)
{
    vec2 vTangentSpaceXY = UnpackFromColor(irradianceDirection.xy);

    vTangentSpaceXY *= (LIGHTMAP_DIRECTION_MAX_XY / max(LIGHTMAP_DIRECTION_MAX_XY, length(vTangentSpaceXY)));

    float sinTheta = length_squared(vTangentSpaceXY);
    float cosTheta = sqrt(1.0 - sinTheta);

    #if (LIGHTMAP_FLATNESS_BOOST)
        // Flat surfaces get a direction pulled toward the extremes, which brings out their normal maps
        float flCurvature = GetCurvature(mat.GeometricNormal, mat.PositionWS);
        float flFlatness = smoothstep(0.1, 0.01, flCurvature) * 0.8;
        float flZScale = mix(1.0, mix(0.1, 2.0, saturate((1.0 - cosTheta) * 1.5)), flFlatness);

        vec3 vTangentSpaceLightVector = normalize(vec3(vTangentSpaceXY, cosTheta * flZScale));
    #else
        vec3 vTangentSpaceLightVector = vec3(vTangentSpaceXY, cosTheta);
    #endif

    vec3 vNonDirectionalLightmap = irradianceColor * saturate(irradianceDirection.z);

    // The final blended normal
    vec3 normalTs = calculateTangentNormal(mat.AmbientNormal, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    return ShadeDirectionalLightmap(irradianceColor, vNonDirectionalLightmap, vTangentSpaceLightVector, vTangentSpaceLightVector.z, normalTs);
}

#else

uniform float g_flDirectionalLightmapStrength = 1.0;
uniform float g_flDirectionalLightmapMinZ = 0.05;

// The direction is used as baked, its z kept above a per material minimum where it is divided by
vec3 ComputeLightmapShading(vec3 irradianceColor, vec3 irradianceDirection, MaterialProperties_t mat)
{
    vec2 directionXY = UnpackFromColor(irradianceDirection.xy);
    vec3 lightVectorTs = vec3(directionXY, sqrt(ClampToPositive(1.0 - length_squared(directionXY))));

    vec3 nonDirectional = irradianceColor * saturate(mix(1.0, irradianceDirection.z, g_flDirectionalLightmapStrength));

    return ShadeDirectionalLightmap(irradianceColor, nonDirectional, lightVectorTs, max(lightVectorTs.z, g_flDirectionalLightmapMinZ), mat.NormalMap);
}

#endif

// Second order spherical harmonics, one set per color channel. The directional textures hold each
// channel's L1 band relative to its L0 band, which the irradiance texture holds. A ZH3 band is
// rebuilt along the luminance weighted L1 direction.
struct LightmapSH_t
{
    vec3 DC;
    vec3 LinearR; // World space
    vec3 LinearG;
    vec3 LinearB;
    vec3 ZonalAxis;
    vec3 ZonalScale; // Per channel, for (3cos^2 - 1) about the axis
};

LightmapSH_t g_LightmapSH = LightmapSH_t(vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0));

vec3 UnpackLightmapSHLinear(vec3 packedBand, float dc)
{
    vec3 linearBand = UnpackFromColor(packedBand) * dc * sqrt(3.0);
    return vec3(-linearBand.z, -linearBand.x, linearBand.y);
}

LightmapSH_t DecodeLightmapSH(vec3 irradianceDC, vec3 packedR, vec3 packedG, vec3 packedB)
{
    LightmapSH_t sh;
    sh.DC = irradianceDC;
    sh.LinearR = UnpackLightmapSHLinear(packedR, irradianceDC.r);
    sh.LinearG = UnpackLightmapSHLinear(packedG, irradianceDC.g);
    sh.LinearB = UnpackLightmapSHLinear(packedB, irradianceDC.b);

    vec3 zonalAxis = GetLuma(sh.LinearR, sh.LinearG, sh.LinearB);
    sh.ZonalAxis = zonalAxis / max(1e-5, length(zonalAxis));

    vec3 axisAlignment = abs(vec3(dot(sh.LinearR, sh.ZonalAxis), dot(sh.LinearG, sh.ZonalAxis), dot(sh.LinearB, sh.ZonalAxis)));
    vec3 zonalRatio = axisAlignment / max(vec3(1e-5), sh.DC);
    sh.ZonalScale = sh.DC * zonalRatio * (0.08 + 0.6 * zonalRatio) * 0.25 * sqrt(5.0 / (16.0 * PI));

    return sh;
}

// Irradiance arriving around a direction. The result already includes the 1/pi of a diffuse BRDF.
vec3 EvaluateLightmapSH(LightmapSH_t sh, vec3 direction)
{
    vec3 linearTerm = (0.886227 * sh.DC) + (1.023328 * vec3(dot(sh.LinearR, direction), dot(sh.LinearG, direction), dot(sh.LinearB, direction)));
    linearTerm /= PI;

    float cosAngle = dot(sh.ZonalAxis, direction);
    vec3 zonalTerm = sh.ZonalScale * (3.0 * cosAngle * cosAngle - 1.0);

    return max(vec3(0.0), linearTerm + zonalTerm);
}

#endif


void CalculateIndirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    lighting.DiffuseIndirect = vec3(0.3);

    // Indirect Lighting
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    if (g_iRenderMode == renderMode_LightmapCharts) {
        vec3 irradiance = texture(g_tLightmapDebugCharts, mat.LightmapUv).rgb;
        lighting.DiffuseIndirect = irradiance;
    } else {
        vec3 irradiance = texture(g_tIrradiance, mat.LightmapUv).rgb;
        #if (LIGHTMAP_SH)
            g_LightmapSH = DecodeLightmapSH(irradiance,
                texture(g_tDirectionalIrradianceR, mat.LightmapUv).rgb,
                texture(g_tDirectionalIrradianceG, mat.LightmapUv).rgb,
                texture(g_tDirectionalIrradianceB, mat.LightmapUv).rgb);
            lighting.DiffuseIndirect = EvaluateLightmapSH(g_LightmapSH, mat.AmbientNormal);
        #else
            vec4 vAHDData = texture(g_tDirectionalIrradiance, mat.LightmapUv);
            lighting.DiffuseIndirect = ComputeLightmapShading(irradiance, vAHDData.xyz, mat);

            #if (LIGHTMAP_SPECULAR_OCCLUSION)
                lighting.SpecularOcclusion = vAHDData.a;
            #endif
        #endif
    }

#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    if (g_iRenderMode == renderMode_LightmapCharts) {
        lighting.DiffuseIndirect = vec3(0);
    } else {
        lighting.DiffuseIndirect = ComputeLightProbeShading(mat);
    }

#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    if (g_iRenderMode == renderMode_LightmapCharts) {
        lighting.DiffuseIndirect = vec3(1);
    } else {
        lighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
    }
#endif

    // Environment Maps
#if defined(IBL) && (IBL == 1)
    EnvBRDF_t envBrdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir, mat.ClothMask);

    // Rough non-metals reflect the lightmap rather than the environment maps
    float lightmapReflection = 0.0;

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LIGHTMAP_SH_REFLECTIONS)
        lightmapReflection = mix(saturate(LightmapReflectionRoughnessRange.x + LightmapReflectionRoughnessRange.y * mat.IsometricRoughness), 0.0, mat.Metalness);
    #endif

    vec3 environment = vec3(0.0);
    float environmentLuma = 0.0;

    if (lightmapReflection < 1.0)
    {
        environment = GetEnvironmentNoBRDF(mat, 1.0);
        environmentLuma = dot(vec4(mat.AmbientNormal, 1.0), g_vEnvMapNormalizationSH);
    }

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LIGHTMAP_SH_REFLECTIONS)
        if (lightmapReflection > 0.0)
        {
            vec3 lightmapSpecular = EvaluateLightmapSH(g_LightmapSH, GetDominantReflectionDirection(mat, mat.AmbientNormal));
            environment = mix(environment, lightmapSpecular, lightmapReflection);
            environmentLuma = mix(environmentLuma, GetLuma(lightmapSpecular), lightmapReflection);
        }
    #endif

    environment *= GetEnvMapNormalization(mat.IsometricRoughness, environmentLuma, lighting.DiffuseIndirect);

    #if (MULTISCATTER_REFLECTS_ENVIRONMENT)
        lighting.SpecularIndirect = environment * (envBrdf.SingleScatter + envBrdf.MultiScatter);
    #else
        lighting.SpecularIndirect = environment * envBrdf.SingleScatter;
    #endif

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0 && D_BAKED_LIGHTING_FROM_PROBE == 0 && D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 0)
        vec2 oldRoughness = mat.Roughness;
        mat.Roughness = vec2(1.0);
        // Roughness is at 1 here, so the full correction lands the lookup on the normal.
        lighting.DiffuseIndirect = GetEnvironmentNoBRDF(mat, 1.0);
        mat.Roughness = oldRoughness;
    #endif

    #if (MULTISCATTER)
        #if !(MULTISCATTER_REFLECTS_ENVIRONMENT)
            lighting.SpecularIndirect += envBrdf.MultiScatter * lighting.DiffuseIndirect;
        #endif

        vec3 diffuseEnergy = vec3(1.0) - (envBrdf.SingleScatter + envBrdf.MultiScatter);

        #if (DIFFUSE_ENERGY_CLAMPED)
            diffuseEnergy = ClampToPositive(diffuseEnergy);
        #endif

        if (F_CLOTH_SHADING == 1)
        {
            diffuseEnergy = mix(diffuseEnergy, vec3(1.0), mat.ClothMask);
        }

        lighting.DiffuseIndirect *= diffuseEnergy;
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

    #if (SPECULAR_ENERGY_COMPENSATION)
        lighting.SpecularDirect *= GetSpecularEnergyCompensation(mat);
    #endif

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
        case renderMode_LightmapCharts:
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
    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (BAKED_LIGHT_SHADOW_MASK)
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
