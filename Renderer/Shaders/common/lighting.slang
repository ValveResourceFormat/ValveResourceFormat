#version 460

#ifndef VRF_LIGHTING_INCLUDE
#define VRF_LIGHTING_INCLUDE

#include "shared_combos.slang"

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
    centroid in vec4 vLightmapUVScaled; // GetLightmapCoordinates
    uniform sampler2DArray g_tIrradiance; // reserved
    uniform sampler2DArray g_tLightmapDebugCharts; // reserved
    #if (LIGHTMAP_SH_LUMINANCE)
        uniform sampler2DArray g_tDirectionalIrradianceLum; // reserved
    #elif (LIGHTMAP_SH)
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
    #include "lighting_lpv.slang"
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    in vec3 vPerVertexLightingOut;
#endif

#include "utils.slang"
#include "texturing.slang"

#include "lighting_common.slang"
#include "shadowmapping.slang"

#include "pbr.slang"
#include "envmap.slang"

#include "lighting_barn.slang"
#include "light_cookie.slang"

#ifndef DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE
    #define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 0 // Lightmap version 1 shades the dynamic lights, not the baked ones, with GetRoughnessShapedDiffuse()
#endif

const float MIN_LIGHT_ALPHA = 0.0001;

void CalculateShadingGated(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat, uint nLightIndex, bool roughnessShapedDiffuse)
{
    vec3 previousDiffuse = lighting.DiffuseDirect;
    vec3 previousSpecular = lighting.SpecularDirect;
    vec3 previousTransmissive = lighting.TransmissiveDirect;

    CalculateShading(lighting, lightVector, lightColor, mat, roughnessShapedDiffuse);

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

#if (LIGHT_GEOMETRIC_NORMAL_GATE) && (!GLASS_BACKLIGHT)
    if (dot(GetSunDirection(), mat.GeometricNormal) <= 0.0)
    {
        return;
    }
#endif

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

// The shadow of a dynamic spot light, rendered into the barn light shadow atlas
float CalculateDynamicLightShadow(uint nShadowSlot, vec3 vPositionWs)
{
    vec4 vClipPos = g_matLightWorldToShadow[nShadowSlot] * vec4(vPositionWs, 1.0);
    vec3 vNdc = vClipPos.xyz / vClipPos.w;

    vec4 vOffsetScale = g_vLightShadowOffsetScale[nShadowSlot];
    vec2 vShadowUv = vNdc.xy * vOffsetScale.zw + vOffsetScale.xy;
    vec2 vTexelSize = 1.0 / vec2(textureSize(g_tBarnLightShadowDepth, 0));

    float flShadowRef = saturate(vNdc.z + SHADOW_RECEIVER_DEPTH_BIAS);

    return ComputeBarnLightShadow_PCF_3x3_Gaussian(vShadowUv, flShadowRef, vTexelSize);
}

// The factor a stationary light's baked strength takes from its real-time shadow, 1 for lights without one
float CalculateStationaryLightRealtimeShadow(uint nLightIndex, vec3 vPositionWs)
{
    float flShadow = 1.0;

    if (float(nLightIndex) == g_vSunLightBakedShadowMask.x)
    {
        flShadow = CalculateSunShadowMapVisibility(vPositionWs);
    }
#if (S_VR_FAST_PATH_LIGHTING)
    else
    {
        int nShadowSlot = int(g_vLightShadowParams[nLightIndex].x);

        if (nShadowSlot >= 0)
        {
            flShadow = CalculateDynamicLightShadow(uint(nShadowSlot), vPositionWs);
        }
    }
#endif

    return flShadow;
}

// Per-pixel dynamic light evaluation for legacy lights
void CalculateDynamicLightV1(inout LightingTerms_t lighting, MaterialProperties_t mat, uint nLightIndex)
{
    uint nLightType = GetLightType(nLightIndex);

    bool bDiffuseOnly = false;

    #if (S_VR_FAST_PATH_LIGHTING)
        // The fast path lights a pixel with one full light and diffuse-only point lights, nothing else
        float flFastPathMode = g_vLightShadowParams[nLightIndex].w;

        if (flFastPathMode < 0.0)
        {
            return;
        }

        bDiffuseOnly = flFastPathMode > 0.0;
    #endif

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

    vec3 vCookie = vec3(1.0);

    if (nLightType == LIGHT_TYPE_SPOT)
    {
        float flCookieSlice = g_vLightShadowParams[nLightIndex].y;

        if (flCookieSlice >= 0.0)
        {
            // The cookie takes the place of the cone falloff
            vCookie = CalculateDynamicLightCookie(nLightIndex, GetLightPositionWs(nLightIndex), mat.PositionWS, flCookieSlice);
        }
        else
        {
            vec2 vConeCosines = g_vLightSpotInnerOuterConeCosines[nLightIndex].xy;
            vec3 vForward = g_vLightDirection_InvRange[nLightIndex].xyz;
            float flCosAngle = dot(vForward, -vLightVector);
            flAttenuation *= saturate((flCosAngle - vConeCosines.y) / max(vConeCosines.x - vConeCosines.y, 0.01));
        }

        int nShadowSlot = int(g_vLightShadowParams[nLightIndex].x);

        if (nShadowSlot >= 0 && flAttenuation > 0.0)
        {
            flAttenuation *= CalculateDynamicLightShadow(uint(nShadowSlot), mat.PositionWS);
        }
    }

    if (flAttenuation <= 0.0)
    {
        return;
    }

    bool roughnessShapedDiffuse = false;

    #if (DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE) && (BAKED_LIGHT_INDICES)
        roughnessShapedDiffuse = true;
    #endif

    if (bDiffuseOnly)
    {
        vec3 vPreviousSpecular = lighting.SpecularDirect;
        vec3 vPreviousTransmissive = lighting.TransmissiveDirect;

        CalculateShadingGated(lighting, vLightVector, GetLightColor(nLightIndex) * flAttenuation, mat, nLightIndex, false);

        lighting.SpecularDirect = vPreviousSpecular;
        lighting.TransmissiveDirect = vPreviousTransmissive;
        return;
    }

    CalculateShadingGated(lighting, vLightVector, GetLightColor(nLightIndex) * flAttenuation * vCookie, mat, nLightIndex, roughnessShapedDiffuse);
}

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (BAKED_LIGHT_SHADOW_MASK)
    // Cubic B-spline filtering from four bilinear taps, to soften the texel steps of baked shadows
    vec4 SampleLightmapShadowsBicubic(vec2 uv, vec2 size)
    {
        vec2 texel = uv * size - 0.5;
        vec2 base = floor(texel);
        vec2 f = texel - base;
        vec2 f2 = f * f;
        vec2 f3 = f2 * f;

        vec2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
        vec2 w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
        vec2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
        vec2 w3 = f3 / 6.0;

        vec2 weight0 = w0 + w1;
        vec2 weight1 = w2 + w3;

        // The taps sit a texel below the usual B-spline positions
        vec2 uv0 = (base + w1 / weight0 - 1.5) / size;
        vec2 uv1 = (base + w3 / weight1 + 0.5) / size;

        vec4 shadows = textureLod(g_tDirectLightShadows, vec3(uv0.x, uv0.y, 0.0), 0.0) * weight0.x * weight0.y;
        shadows += textureLod(g_tDirectLightShadows, vec3(uv1.x, uv0.y, 0.0), 0.0) * weight1.x * weight0.y;
        shadows += textureLod(g_tDirectLightShadows, vec3(uv0.x, uv1.y, 0.0), 0.0) * weight0.x * weight1.y;
        shadows += textureLod(g_tDirectLightShadows, vec3(uv1.x, uv1.y, 0.0), 0.0) * weight1.x * weight1.y;

        return shadows;
    }

    vec4 SampleLightmapShadows(vec2 uv)
    {
        #if (LIGHTMAP_BICUBIC_SHADOWS)
            vec2 size = vec2(textureSize(g_tDirectLightShadows, 0).xy);

            // Triangles with collapsed lightmap coordinates keep the plain sample
            vec2 hashChange = fwidth(vLightmapUVScaled.zw);
            bool bicubic = min(hashChange.x, hashChange.y) > 0.1 && size.x > LIGHTMAP_BICUBIC_SHADOWS_MIN_WIDTH;

            if (bicubic)
            {
                return SampleLightmapShadowsBicubic(uv, size);
            }
        #endif

        return textureLod(g_tDirectLightShadows, vec3(uv, 0.0), 0.0);
    }
#endif

// Dynamic (directlight 2) lights, evaluated per pixel.
void CalculateDynamicLights(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    for (uint i = g_nNumLights[0]; i < g_nNumLights[1]; i++)
    {
        CalculateDynamicLightV1(lighting, mat, i);
    }
}

#if (BAKED_LIGHT_INDICES)

// Shades the stationary lights that the lightmap or probe references by index, and the sun where it bakes none
void CalculateBakedIndexedLights(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
#if (SUBSURFACE_SKIN)
    vec3 previousTransmissive = lighting.TransmissiveDirect;
#endif

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
            CalculateShadingGated(lighting, lightVector, GetLightColor(uLightIndex) * visibility, mat, uLightIndex, false);
        }
    }
#else
    CalculateSunLighting(lighting, mat, 1.0);
#endif

#if (SUBSURFACE_SKIN)
    // Skin passes the light of the dynamic lights only, which have a shadow map to measure the thickness from
    lighting.TransmissiveDirect = previousTransmissive;
#endif
}

#elif (BAKED_LIGHT_SHADOW_MASK)

// The baked shadow channels of the lights, unshadowed where nothing baked them
vec4 SampleBakedLightShadows(MaterialProperties_t mat)
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    return SampleLightmapShadows(mat.LightmapUv.xy);
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    return SampleProbeDirectLightShadows(mat.PositionWS, mat.AmbientNormal);
#else
    return vec4(0.0);
#endif
}

#endif

void CalculateDirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
#if (BAKED_LIGHT_INDICES)
    // Scenes with baked light indices light through those and the dynamic lights instead of the barn lights
    CalculateBakedIndexedLights(lighting, mat);
    CalculateDynamicLights(lighting, mat);
#elif (BAKED_LIGHT_SHADOW_MASK)
    vec4 bakedShadows = SampleBakedLightShadows(mat);

    CalculateSunLighting(lighting, mat, saturate(1.0 - dot(bakedShadows, g_vSunLightBakedShadowMask)));
    CalculateBarnLighting(lighting, mat, bakedShadows);
#else
    // Non lightmapped scene
    CalculateSunLighting(lighting, mat, 1.0);
    CalculateDynamicLights(lighting, mat);
    CalculateBarnLighting(lighting, mat, vec4(0.0));
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

    float sinThetaSquared = length_squared(vTangentSpaceXY);
    float cosTheta = sqrt(1.0 - sinThetaSquared);

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
    vec3 normalTs = CalculateTangentNormal(mat.AmbientNormal, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
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

void CalculateLightmapIndirectLighting(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    if (g_iRenderMode == renderMode_LightmapCharts)
    {
        lighting.DiffuseIndirect = texture(g_tLightmapDebugCharts, mat.LightmapUv).rgb;
        return;
    }

    vec3 irradiance = texture(g_tIrradiance, mat.LightmapUv).rgb;

#if (LIGHTMAP_NON_DIRECTIONAL)
    // Lights the surface the same from either side, as two sided foliage is lit
    lighting.DiffuseIndirect = irradiance;
#elif (LIGHTMAP_SH_LUMINANCE)
    // One direction relative to luminance, shared by every channel
    vec3 packedDirection = texture(g_tDirectionalIrradianceLum, mat.LightmapUv).rgb;
    g_LightmapSH = DecodeLightmapSH(irradiance, packedDirection, packedDirection, packedDirection);
    lighting.DiffuseIndirect = EvaluateLightmapSH(g_LightmapSH, mat.AmbientNormal);
#elif (LIGHTMAP_SH)
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

#endif

#if (SUBSURFACE_SKIN) && (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0) && (D_BAKED_LIGHTING_FROM_PROBE == 1)
vec3 ComputeSkinLightProbeShading(MaterialProperties_t mat)
{
    vec3 irradiance;

    for (int i = 0; i < 3; i++)
    {
        vec3 channelNormal = normalize(mix(mat.AmbientNormal, mat.AmbientGeometricNormal, g_vAmbientNormalSoftness[i]));
        irradiance[i] = SampleLightProbeIrradiance(mat.PositionWS, channelNormal)[i];
    }

    return irradiance;
}
#endif

void CalculateBakedIndirectLighting(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
#if (INDIRECT_LIGHT_TEXTURE)
    lighting.DiffuseIndirect = mat.IndirectLightTexture;
#elif (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    CalculateLightmapIndirectLighting(lighting, mat);
#elif (D_BAKED_LIGHTING_FROM_PROBE == 1)
    if (g_iRenderMode == renderMode_LightmapCharts)
    {
        lighting.DiffuseIndirect = vec3(0);
        return;
    }

    #if (SUBSURFACE_SKIN)
        lighting.DiffuseIndirect = ComputeSkinLightProbeShading(mat);
    #else
        lighting.DiffuseIndirect = ComputeLightProbeShading(mat);
    #endif
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    if (g_iRenderMode == renderMode_LightmapCharts)
    {
        lighting.DiffuseIndirect = vec3(1);
        return;
    }

    lighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
#endif
}

#if (IBL == 1)

// Rough non-metals reflect the lightmap rather than the environment maps
float GetLightmapReflection(MaterialProperties_t mat)
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LIGHTMAP_SH)
    return mix(saturate(LightmapReflectionRoughnessRange.x + LightmapReflectionRoughnessRange.y * mat.IsometricRoughness), 0.0, mat.Metalness);
#else
    return 0.0;
#endif
}

void BlendLightmapReflection(inout vec3 environment, inout float environmentLuma, MaterialProperties_t mat, float lightmapReflection)
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LIGHTMAP_SH)
    if (lightmapReflection > 0.0)
    {
        vec3 lightmapSpecular = EvaluateLightmapSH(g_LightmapSH, GetDominantReflectionDirection(mat, mat.AmbientNormal));
        environment = mix(environment, lightmapSpecular, lightmapReflection);
        environmentLuma = mix(environmentLuma, GetLuma(lightmapSpecular), lightmapReflection);
    }
#endif
}

vec3 GetEnvironmentReflectionScale(EnvBRDF_t envBrdf)
{
#if (MULTISCATTER_REFLECTS_ENVIRONMENT)
    return envBrdf.SingleScatter + envBrdf.MultiScatter;
#else
    return envBrdf.SingleScatter;
#endif
}

// Without baked lighting the environment alone lights the diffuse
void ApplyEnvironmentDiffuse(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
#if (INDIRECT_LIGHT_TEXTURE)
    lighting.DiffuseIndirect = mat.IndirectLightTexture;
#elif (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0 && D_BAKED_LIGHTING_FROM_PROBE == 0 && D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 0)
    #if (F_USE_NPR_LIGHTING == 1)
        // The bounce light already comes from the sky
        if (g_bNPRBounceDiffuse)
        {
            return;
        }
    #endif

    #if (S_VR_FAST_PATH_LIGHTING)
        lighting.DiffuseIndirect = GetSkyAmbientLighting(mat.AmbientNormal);
        return;
    #endif

    mat.Roughness = vec2(1.0);
    #if (ENV_SPECULAR_SIMPLE)
        mat.IsometricRoughness = 1.0;
    #endif
    mat.RetroReflectivity = 0.0;
    // Roughness is at 1 here, so the full correction lands the lookup on the normal.
    lighting.DiffuseIndirect = GetEnvironmentNoBRDF(mat, 1.0);
#endif
}

void ApplyMultiscatter(inout LightingTerms_t lighting, EnvBRDF_t envBrdf, MaterialProperties_t mat)
{
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
}

#if (ENV_SPECULAR_SCHLICK)
// How far the surface is turned from the eye, raised to the power that Schlick's fresnel rises by
float GetSchlickGrazing(MaterialProperties_t mat)
{
    float turned = 1.0 - saturate(dot(mat.ViewDir, mat.AmbientNormal));

    return pow(turned, mat.FresnelExponent);
}

// The fresnel of the reflectance by the angle to the eye, which metals tint with the albedo
vec3 GetSchlickEnvironmentScale(MaterialProperties_t mat)
{
    float fresnel = mat.Reflectance + (1.0 - mat.Reflectance) * GetSchlickGrazing(mat);

    return fresnel * mix(vec3(1.0), mat.Albedo, mat.Metalness);
}

void CalculateEnvironmentLighting(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    lighting.SpecularIndirect = GetProbeReflection(mat) * GetSchlickEnvironmentScale(mat);
}
#else
void CalculateEnvironmentLighting(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    EnvBRDF_t envBrdf = EnvBRDF(mat.SpecularColor, mat.IsometricRoughness, mat.AmbientNormal, mat.ViewDir, mat.ClothMask);

    float lightmapReflection = GetLightmapReflection(mat);

    vec3 environment = vec3(0.0);
    float environmentLuma = 0.0;

    if (lightmapReflection < 1.0)
    {
        environment = GetEnvironmentNoBRDF(mat, 1.0);
        environmentLuma = dot(vec4(mat.AmbientNormal, 1.0), g_vEnvMapNormalizationSH);
    }

    BlendLightmapReflection(environment, environmentLuma, mat, lightmapReflection);

    vec3 specular = environment * GetEnvironmentReflectionScale(envBrdf);
    float normalizationRoughness = mat.IsometricRoughness;

    #if (SECOND_SPECULAR_LOBE)
        MaterialProperties_t secondLobe = mat;
        float secondRoughness = max(mat.RoughnessTex.x * (1.0 - g_flSecondSpecularLobeGlossiness), mat.GeometricRoughness);
        secondLobe.Roughness = vec2(secondRoughness);
        secondLobe.IsometricRoughness = secondRoughness;

        EnvBRDF_t secondBrdf = EnvBRDF(secondLobe.SpecularColor, secondRoughness, mat.AmbientNormal, mat.ViewDir, mat.ClothMask);
        vec3 secondEnvironment = vec3(0.0);
        float secondEnvironmentLuma = 0.0;

        if (lightmapReflection < 1.0)
        {
            secondEnvironment = GetEnvironmentNoBRDF(secondLobe, 1.0);
        }

        BlendLightmapReflection(secondEnvironment, secondEnvironmentLuma, secondLobe, lightmapReflection);

        specular = mix(specular, secondEnvironment * GetEnvironmentReflectionScale(secondBrdf), g_flSecondSpecularLobeRatio);
        normalizationRoughness = (mat.IsometricRoughness + secondRoughness) * 0.5;
    #endif

    lighting.SpecularIndirect = specular * GetEnvMapNormalization(normalizationRoughness, environmentLuma, lighting.DiffuseIndirect);

    ApplyEnvironmentDiffuse(lighting, mat);
    ApplyMultiscatter(lighting, envBrdf, mat);
}
#endif

#endif

#if (F_USE_NPR_LIGHTING == 1)

// The ambient cube around the surface, from the probe volumes or else from the environment diffuse
void GetNprAmbientFaces(MaterialProperties_t mat, out vec3 faces[6])
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0) && (D_BAKED_LIGHTING_FROM_PROBE == 1)
    SampleLightProbeFaces(mat.PositionWS, mat.AmbientNormal, faces);
#else
    mat.Roughness = vec2(1.0);
    mat.RetroReflectivity = 0.0;

    for (int face = 0; face < 6; face++)
    {
        vec3 axis = vec3(0.0);
        axis[face % 3] = face < 3 ? 1.0 : -1.0;

        mat.Normal = axis;
        mat.AmbientNormal = axis;
        faces[face] = GetEnvironmentNoBRDF(mat, 1.0);
    }
#endif
}

vec3 EvaluateNprAmbientCube(vec3 faces[6], vec3 direction)
{
    vec3 squared = direction * direction;
    vec3 positive = step(-direction, vec3(0.0)) * squared;
    vec3 negative = step(direction, vec3(0.0)) * squared;

    return faces[0] * positive.x + faces[1] * positive.y + faces[2] * positive.z
        + faces[3] * negative.x + faces[4] * negative.y + faces[5] * negative.z;
}

// Replaces the diffuse indirect light with a bounce light taken from the cube in a direction that turns
// from the ground to the camera side to the sky as the surface faces the primary light.
// The rim light is added to the transmissive light, which the albedo does not tint.
void ApplyNprBounce(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    vec3 bounce = lighting.DiffuseIndirect;

    if (g_bNPRBounceDiffuse)
    {
        vec3 faces[6];
        GetNprAmbientFaces(mat, faces);

        vec3 average = (faces[0] + faces[1] + faces[2] + faces[3] + faces[4] + faces[5]) / 6.0;

        // The screen space occlusion is not modelled, so only the texture darkens the lit side
        float sunVisibility = 1.0;

        if (GetSunColor() != vec3(0.0))
        {
            sunVisibility = CalculateSunShadowMapVisibility(mat.PositionWS);
        }

        float halfLambert = 0.5 + 0.5 * dot(GetNprPrimaryLightDirection(), mat.Normal);

        if (halfLambert > 0.5)
        {
            halfLambert = mix(0.5, halfLambert, sunVisibility);
        }

        float occlusionRemap = saturate(1.0 / g_vNPRDfaoInfluenceRange.y) * (1.0 - g_vNPRDfaoInfluenceRange.x) + g_vNPRDfaoInfluenceRange.x;
        float occlusion = min(mat.AmbientOcclusion, occlusionRemap);

        float lit = saturate(halfLambert * occlusion * 2.0 - 0.5);
        float stepped = NprStep(lit, g_flNPRDiffuseStepSharpness);

        vec3 horizontal = normalize(vec3(mat.ViewDir.xy, 0.0));
        vec3 bounceDirection;

        if (stepped < 0.5)
        {
            bounceDirection = mix(vec3(0.0, 0.0, -1.0), horizontal, stepped + 0.5);
        }
        else
        {
            bounceDirection = mix(horizontal, vec3(0.0, 0.0, 1.0), stepped - 0.5);
        }

        bounceDirection = normalize(bounceDirection);

        bounce = EvaluateNprAmbientCube(faces, bounceDirection);
        vec3 skyLight = faces[2];

        if (g_bNPRExposureControl)
        {
            vec3 averageXyY = NprRgbToXyY(average);
            float upTarget = bounceDirection.z > 0.0 ? g_vNPRExposureTargets.x : g_vNPRExposureTargets.z;
            vec3 targets = vec3(g_vNPRExposureTargets.y, g_vNPRExposureTargets.y, upTarget);

            bounce = NprControlExposure(bounce, averageXyY, dot(targets, bounceDirection * bounceDirection) / NPR_EXPOSURE);
            skyLight = NprControlExposure(skyLight, averageXyY, g_vNPRExposureTargets.x / NPR_EXPOSURE);
        }

        bounce += skyLight * mat.TransmissiveColor * (1.0 - (stepped + 0.5) * 0.5);
    }

    float rim = NprRimFactor(mat.PositionWS, mat.Normal, mat.ViewDir, mat.AmbientOcclusion, mat.RimLightMask);
    lighting.TransmissiveDirect += (lighting.DiffuseDirect + bounce) * rim;

    lighting.DiffuseIndirect = bounce;
}

#endif

#if (ENV_SPECULAR_SCHLICK)
// The baked light is not given the share of the reflectance that the direct light is, so it takes back what DiffuseColor
// gave up. Where an environment map is reflected, the fresnel gives up the part that the angle to the eye adds.
float GetBakedDiffuseScale(MaterialProperties_t mat)
{
    #if (IBL == 1)
        return 1.0 - GetSchlickGrazing(mat);
    #else
        return 1.0 / max(1.0 - mat.Reflectance, 0.05);
    #endif
}
#endif

void CalculateIndirectLighting(inout LightingTerms_t lighting, MaterialProperties_t mat)
{
    lighting.DiffuseIndirect = vec3(0.3);

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1) && (TRANSMISSIVE_GEOMETRIC_NORMAL) && (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0) && (D_BAKED_LIGHTING_FROM_PROBE == 1)
    // The probe light behind the surface passes through it, without ambient occlusion
    lighting.TransmissiveDirect += SampleLightProbeIrradiance(mat.PositionWS, -mat.GeometricNormal) * mat.TransmissiveColor;
#endif

    CalculateBakedIndirectLighting(lighting, mat);

#if (F_USE_NPR_LIGHTING == 1)
    ApplyNprBounce(lighting, mat);
#endif

#if (IBL == 1)
    CalculateEnvironmentLighting(lighting, mat);
#endif

#if (ENV_SPECULAR_SCHLICK)
    lighting.DiffuseIndirect *= GetBakedDiffuseScale(mat);
#endif

#if (F_USE_NPR_LIGHTING == 1)
    lighting.DiffuseIndirect *= NprMultiBounceOcclusion(mat.Albedo, mat.AmbientOcclusion);
#endif
}

// The direct weights scale how much the diffuse and specular AO of the material darken the direct light
void ApplyAmbientOcclusion(inout LightingTerms_t o, MaterialProperties_t mat, float directDiffuseAO, float directSpecularAO)
{
#if defined(DIFFUSE_AO_COLOR_BLEED)
    SetDiffuseColorBleed(mat);
#endif

    // In non-lightmap shaders, SpecularAO always does a min(1.0, specularAO) in the same place where lightmap
    // shaders do min(bakedAO, specularAO). That means that bakedAO exists and is a constant 1.0 in those shaders!
    mat.SpecularAO = min(o.SpecularOcclusion, mat.SpecularAO);

    vec3 DirectAODiffuse = mix(vec3(1.0), mat.DiffuseAO, directDiffuseAO);
    float DirectAOSpecular = mix(1.0, mat.SpecularAO, directSpecularAO);

    o.DiffuseDirect *= DirectAODiffuse;
    o.DiffuseIndirect *= mat.DiffuseAO;
    o.SpecularDirect *= DirectAOSpecular;
    o.SpecularIndirect *= mat.SpecularAO;
}

LightingTerms_t CalculateLighting(inout MaterialProperties_t mat)
{
    LightingTerms_t lighting = InitLighting();

    #if (ROUGHNESS_FLOOR_OPAQUE && (F_TRANSLUCENT != 1) && (F_GLASS != 1))
        mat.Roughness = max(mat.Roughness, vec2(0.05));
    #endif

    #if (ENV_SPECULAR_SIMPLE)
        mat.IsometricRoughness = dot(mat.Roughness, vec2(0.5)) * mat.EnvironmentBlur;
    #elif ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
        mat.IsometricRoughness = dot(mat.Roughness, vec2(0.5));
    #else
        mat.IsometricRoughness = max(mat.Roughness.x, mat.Roughness.y);
    #endif

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        mat.LightmapUv = vec3(vLightmapUVScaled.xy, 0.0);
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

bool HandleLightingRenderModesLinear(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
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
            float flLightmapOcclusion = Blink() ? 1.0 : lighting.SpecularOcclusion;
            outputColor = vec4(SrgbGammaToLinear((mat.AmbientOcclusion * flLightmapOcclusion).xxx), 1.0);
            return true;
        case renderMode_LightTiles:
            vec3 lightTileHeat;
            outputColor.rgb *= GetLightCullBinStripe(mat.PositionWS);

            if (GetTileHeatmap(g_nBarnLightsIterated, 12.0, lightTileHeat))
            {
                outputColor.rgb = mix(outputColor.rgb, lightTileHeat, 0.5);
            }
            return true;
        case renderMode_EnvmapTiles:
            vec3 envMapTileHeat;
            outputColor.rgb *= GetLightCullBinStripe(mat.PositionWS);

            if (GetTileHeatmap(uint(g_nEnvMapsIterated), 4.0, envMapTileHeat))
            {
                outputColor.rgb = mix(outputColor.rgb, envMapTileHeat, 0.5);
            }
            return true;
        case renderMode_Subgroups:
            outputColor = vec4(GetSubgroupFootprintColor(), 1.0);
            return true;
    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (BAKED_LIGHT_SHADOW_MASK)
        case renderMode_LightmapShadows:
            vec4 dlsh = SampleLightmapShadows(mat.LightmapUv.xy);
            outputColor = vec4(vec3(1.0 - dlsh.x) + vec3(1.0 - Min3(dlsh.yzw)) * vec3(0.5, 0.5, 0), 1.0);
            return true;
    #endif
        default:
            return false;
    }
}

bool HandleLightingRenderModes(inout vec4 outputColor, MaterialProperties_t mat, LightingTerms_t lighting)
{
    if (HandleLightingRenderModesLinear(outputColor, mat, lighting))
    {
        outputColor.rgb = SrgbLinearToGamma(outputColor.rgb);
        return true;
    }

    return false;
}

#endif
