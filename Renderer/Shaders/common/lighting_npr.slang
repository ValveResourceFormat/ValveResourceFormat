#version 460

#ifndef VRF_LIGHTING_NPR_INCLUDE
#define VRF_LIGHTING_NPR_INCLUDE

#include "utils.slang"
#include "depth.slang"

// Stylized lighting of Deadlock heroes. The parameters are global render attributes the game publishes
// from its NPR settings material, the defaults here are the values that material ships with.

uniform bool g_bNPRBounceDiffuse = true;
uniform float g_flNPRDiffuseStepSharpness = 0.9;
uniform vec2 g_vNPRDfaoInfluenceRange = vec2(0.4, 1.0);
uniform float g_flNPRDiffusePbrBlend = 0.25;
uniform float g_flNPRPrimaryLightCameraWeight = 0.35;

uniform bool g_bNPRDirectDiffuse = true;
uniform float g_flNPRDirectLightWrap = 0.8;

uniform bool g_bNPRExposureControl = true;
uniform vec3 g_vNPRExposureTargets = vec3(1.0, 0.5, 0.1);
uniform float g_flNPRExposureControlPbrBlend = 0.0;

uniform bool g_bNPRRimLighting = true;
uniform float g_flNPRRimLightFalloff = 0.7;
uniform float g_flNPRRimLightWrap = 0.7;
uniform float g_flNPRRimLightStrength = 2.0;
uniform vec2 g_vNPRRimLightUpRamp = vec2(0.2, 0.2);
uniform bool g_bNPRRimLightingDepthOcclusion = true;
uniform float g_flNPRRimOcclusionSampleDistance = 0.5;
uniform float g_flNPRRimOcclusionDepthToleranceMin = 2.5;
uniform float g_flNPRRimOcclusionDepthToleranceRange = 0.5;

// Exposure the game divides its luminance targets by, the scene here is not pre-exposed
const float NPR_EXPOSURE = 1.0;

// Steps x to its whole part and bends the fraction towards the nearest whole number. A sharpness
// towards 1 narrows the transition around the half way point.
float NprStep(float x, float sharpness)
{
    float whole = floor(x);
    float fraction = x - whole;
    float exponent = 1.0 / (1.0 - sharpness);
    bool upperHalf = fraction > 0.5;
    float distanceToWhole = upperHalf ? 1.0 - fraction : fraction;
    float bent = pow(0.5, 1.0 - exponent) * pow(distanceToWhole, exponent);

    return whole + (upperHalf ? 1.0 - bent : bent);
}

// What the diffuse term of a light is, from the cosine of the angle to it
float NprDirectDiffuse(float NoL)
{
    if (!g_bNPRDirectDiffuse)
    {
        return NoL;
    }

    float normalization = 1.0 / (2.0 * g_flNPRDirectLightWrap);
    float wrapped = saturate(0.5 + ((g_flNPRDirectLightWrap - 0.5 + NoL) - 0.5) * 2.0);

    return mix(normalization * NprStep(wrapped, g_flNPRDiffuseStepSharpness), NoL, g_flNPRDiffusePbrBlend);
}

// The direction the bounce light is taken to come from, straight above turned towards the camera side
vec3 GetNprPrimaryLightDirection()
{
    vec3 towardsCamera = normalize(vec3(-g_vCameraDirWs.xy, 1e-8));

    return normalize(mix(vec3(0.0, 0.0, 1.0), towardsCamera, g_flNPRPrimaryLightCameraWeight));
}

vec3 NprRgbToXyY(vec3 rgb)
{
    float X = dot(vec3(0.4124, 0.3576, 0.1805), rgb);
    float Y = dot(vec3(0.2126, 0.7152, 0.0722), rgb);
    float Z = dot(vec3(0.0193, 0.1192, 0.9505), rgb);
    float sum = X + Y + Z;

    if (sum <= 0.0)
    {
        return vec3(0.0);
    }

    return vec3(X / sum, Y / sum, Y);
}

vec3 NprXyYToRgb(vec3 xyY)
{
    if (any(equal(xyY, vec3(0.0))))
    {
        return vec3(0.0);
    }

    float scale = xyY.z / xyY.y;
    float X = scale * xyY.x;
    float Y = xyY.z;
    float Z = scale * (1.0 - xyY.x - xyY.y);

    return vec3(
        3.2406 * X - 1.5372 * Y - 0.4986 * Z,
        -0.9689 * X + 1.8758 * Y + 0.0415 * Z,
        0.0557 * X - 0.2040 * Y + 1.0570 * Z);
}

// Pulls the luminance of light to the target, and its chroma to the average of the surroundings where it is dim
vec3 NprControlExposure(vec3 light, vec3 averageXyY, float targetLuminance)
{
    vec3 lightXyY = NprRgbToXyY(light);

    if (averageXyY.z <= 0.0)
    {
        return light;
    }

    vec2 chroma = mix(averageXyY.xy, lightXyY.xy, saturate(lightXyY.z / averageXyY.z));
    vec3 controlled = NprXyYToRgb(vec3(chroma, targetLuminance));

    return mix(controlled, light, g_flNPRExposureControlPbrBlend);
}

// Multi bounce fit of the occlusion on an albedo
vec3 NprMultiBounceOcclusion(vec3 albedo, float occlusion)
{
    float occlusionSquared = occlusion * occlusion;
    vec3 cubic = (albedo * 2.0404 - 0.3324) * (occlusionSquared * occlusion);
    vec3 quadratic = (albedo * 4.7951 - 0.6417) * occlusionSquared;
    vec3 linear = (albedo * 2.7552 + 0.6903) * occlusion;

    return mix(vec3(1.0), cubic - quadratic + linear, g_flNPRDiffusePbrBlend);
}

// Fades the rim by where the surface faces: an empty range is a step at its start
float NprRimUpRamp(float normalZ)
{
    float range = g_vNPRRimLightUpRamp.y - g_vNPRRimLightUpRamp.x;

    if (range == 0.0)
    {
        return float(normalZ > g_vNPRRimLightUpRamp.x);
    }

    return saturate((normalZ - g_vNPRRimLightUpRamp.x) / range);
}

// The distance along the view direction to what the scene depth holds at a pixel, infinite at the far plane
float GetNprSceneViewDepth(ivec2 pixel)
{
    ivec2 texel = min(pixel * ivec2(g_vSceneDepthFetchScale), textureSize(g_tSceneDepth, 0) - 1);
    float sceneDepth = texelFetch(g_tSceneDepth, texel, 0).x;
    float normalizedDepth = RemapValClamped(sceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

    return 1.0 / fma(normalizedDepth, g_vInvProjRow3.z, g_vInvProjRow3.w);
}

// Keeps the rim only where the scene a short step out from the surface lies well behind it, which is
// along the silhouette. The scene depth is the one resolved after the opaque pass, so the surface reads
// the depth of the previous frame and the rim trails a moving camera by one frame.
float NprRimDepthOcclusion(vec3 positionWs, vec3 normal)
{
    if (!g_bNPRRimLightingDepthOcclusion)
    {
        return 1.0;
    }

    vec3 samplePosition = positionWs + normalize(mix(normal, vec3(0.0, 0.0, 1.0), 0.5)) * g_flNPRRimOcclusionSampleDistance;
    vec4 sampleClip = g_matWorldToProjection * vec4(samplePosition, 1.0);
    vec2 sampleUv = saturate(sampleClip.xy / sampleClip.w * 0.5 + 0.5);
    ivec2 samplePixel = ivec2(sampleUv * g_vViewportSize);

    float surfaceDepth = GetNprSceneViewDepth(ivec2(gl_FragCoord.xy));
    float sampleDepth = GetNprSceneViewDepth(samplePixel);
    float behind = sampleDepth - (surfaceDepth + g_flNPRRimOcclusionDepthToleranceMin);

    return smoothstep(0.0, 1.0, saturate(behind / g_flNPRRimOcclusionDepthToleranceRange));
}

float NprRimFactor(vec3 positionWs, vec3 normal, vec3 viewDir, float occlusion, float rimMask)
{
    if (!g_bNPRRimLighting || rimMask <= 0.0)
    {
        return 0.0;
    }

    float wrap = g_flNPRRimLightWrap;
    float facing = pow(saturate((wrap - dot(normal, viewDir)) / pow2(1.0 + wrap)), g_flNPRRimLightFalloff);
    float rim = facing * NprRimUpRamp(normal.z) * g_flNPRRimLightStrength * occlusion * rimMask;

    if (rim <= 0.0)
    {
        return 0.0;
    }

    return rim * NprRimDepthOcclusion(positionWs, normal);
}

#endif
