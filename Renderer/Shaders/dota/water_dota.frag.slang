#version 460

#define F_FLOW_NORMALS 0
#define F_FORCE_CHEAP_WATER 0
#define F_FORCE_FOG_OVERRIDE 0
#define F_FORCE_NULL_WATER 0

#include "../common/utils.slang"

in vec3 vFragPosition;

out vec4 outputColor;

#if (F_FLOW_NORMALS == 1)
    uniform sampler2D g_tFlow;
    uniform sampler2D g_tNoise;
#endif

uniform sampler2D g_tNormal;

#include "../common/view_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/depth.slang"

#if (F_FORCE_CHEAP_WATER == 0)
    uniform sampler2D g_tSceneColor; // reserved
#endif

uniform float g_flBumpStrength = 1.0;
uniform float g_flScroll1X;
uniform float g_flScroll1Y;
uniform float g_flScroll2X;
uniform float g_flScroll2Y;

uniform float g_flFlowTimeScale = 1.0;
uniform float g_flWorldPositionScale = 1.0;
uniform float g_flNormalUvScale = 1.0;
uniform float g_flNormalFlowLerpExp = 1.0;
uniform float g_flNormalFlowTimeIntervalInSeconds = 0.4;
uniform float g_flNormalFlowUvScrollDistance = 0.25;
uniform float g_flNoiseStrength = 1.0;
uniform float g_flNoiseUvScale = 1.0;

uniform float g_flLowEndBumpStrength = 0.15;
uniform float g_flLowEndReflectionPower = 1.0;
uniform float g_flLowEndSurfaceFactor = 0.3;
uniform float g_flLowEndSurfaceMinimumColor = 0.2;
uniform float g_flLowEndSurfaceOpacity = 0.5;
uniform vec3 g_vLowEndReflectionColor = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vLowEndReflectionDir = vec3(0.0, 0.0, 1.0);
uniform vec3 g_vLowEndSurfaceColor = vec3(0.214041); // SrgbRead(true)

uniform float g_flReflectance = 0.2;
uniform float g_flReflectionPower = 1.0;
uniform float g_flRefractionAmount = 1.0;
uniform float g_flWaterDepth = 35.0;
uniform float g_flWaterFogShadowFactor = 1.0;
uniform vec3 g_vReflectionColor = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vReflectionDir = vec3(0.0, 0.0, 1.0);
uniform vec3 g_vReflectionTint = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vRefractionTint = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vWaterFogColor = vec3(0.5); // SrgbRead(true)

#if (F_FORCE_FOG_OVERRIDE == 1)
    uniform float g_flForceFogOverrideMin;
    uniform float g_flForceFogOverrideMax;
#endif

// World extent covered by g_tFlow and centred on the origin, in units.
// TODO: read it from the fog_mins and fog_maxs light info keys; this is the dota map's fog bounds.
const float FlowMapWorldSize = 18944.0;

// Normal map uv shift per flow cycle
const float FlowCycleUvShift = 0.311;

// Refraction and reflection uv offset per unit of normal, before the division by the camera distance
const float ScreenOffsetScale = 70.0 * 16.0;

// The uv in g_tFlow of a world position
vec2 GetFlowUv(vec3 position)
{
    return (position.xy - 0.5) / FlowMapWorldSize + 0.5;
}

// Cross-fades two normal map layers whose flow cycles are half a period apart
vec2 SampleFlowNormal(vec2 uv, vec2 flow, float phase)
{
    float cycleA = fract(phase);
    float cycleB = fract(phase + 0.5);
    float shiftA = floor(phase) * FlowCycleUvShift;
    float shiftB = floor(phase + 0.5) * FlowCycleUvShift + 0.5;
    vec2 scroll = flow * g_flNormalFlowUvScrollDistance;

    float weightA = pow(abs(2.0 * cycleB - 1.0), g_flNormalFlowLerpExp);
    float weightB = pow(abs(2.0 * cycleA - 1.0), g_flNormalFlowLerpExp);

    vec2 normalA = texture(g_tNormal, uv + shiftA + scroll * cycleA).wy;
    vec2 normalB = texture(g_tNormal, uv + shiftB + scroll * cycleB).wy;

    return normalA * weightA + normalB * weightB;
}

vec3 GetSurfaceNormal()
{
    vec2 worldUv = vec2(vFragPosition.x, -vFragPosition.y) * g_flWorldPositionScale;
    vec2 normalUv = worldUv / g_flNormalUvScale;

    vec2 slope;

#if (F_FLOW_NORMALS == 1)
    vec2 flow = texture(g_tFlow, GetFlowUv(vFragPosition)).xy * 2.0 - 1.0;

    float noise = texture(g_tNoise, worldUv * g_flNoiseUvScale).y;
    float phase = g_flTime * g_flFlowTimeScale / (g_flNormalFlowTimeIntervalInSeconds * 2.0) + noise * g_flNoiseStrength;

    slope = SampleFlowNormal(normalUv, flow, phase) * 2.0 - 1.0;
    slope *= dot(flow, flow) + 0.1;
#else
    float time = g_flTime * g_flFlowTimeScale;
    vec4 normalA = texture(g_tNormal, normalUv + vec2(g_flScroll1X, g_flScroll1Y) * time);
    vec4 normalB = texture(g_tNormal, normalUv.yx + vec2(g_flScroll2Y, g_flScroll2X) * time);

    slope = (normalA + normalB).wy - 1.0;
#endif

    slope *= g_flBumpStrength;

#if (F_FORCE_CHEAP_WATER == 1)
    slope *= g_flLowEndBumpStrength;
#endif

    if (dot(slope, slope) > 1.0)
    {
        slope = normalize(slope);
    }

    float z = sqrt(saturate(1.0 - dot(slope, slope)));
    return vec3(slope, z);
}

#if (F_FORCE_CHEAP_WATER == 1)

vec4 ShadeCheapWater(vec3 normal, vec3 viewDirection)
{
    vec3 reflection = reflect(-viewDirection, normal);
    float reflectionFactor = saturate(dot(g_vLowEndReflectionDir, reflection));
    float highlight = pow(reflectionFactor, g_flLowEndReflectionPower);

    float surfaceFactor = saturate(pow(1.0 - normal.z, g_flLowEndSurfaceFactor) + g_flLowEndSurfaceMinimumColor);
    vec3 surfaceColor = g_vLowEndSurfaceColor * surfaceFactor;

    vec3 color = saturate(surfaceColor + g_vLowEndReflectionColor * highlight);
    float opacity = saturate(highlight + g_flLowEndSurfaceOpacity);

    return vec4(color, opacity);
}

#else

const vec3 Up = vec3(0.0, 0.0, 1.0);

// The depth of the scene below the water surface over g_flWaterDepth, from 0 to 1, which the game reads from the scene color alpha
float GetWaterDepthFactor(vec3 cameraRay)
{
    float sceneDepth = textureLod(g_tSceneDepth, gl_FragCoord.xy / vec2(textureSize(g_tSceneDepth, 0)), 0.0).x;
    float normalizedDepth = max(LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, sceneDepth), 0.00001);
    vec3 scenePosition = GetWorldPositionFromNormalizedDepth(normalizedDepth, cameraRay);

    float depthFactor = saturate((vFragPosition.z - scenePosition.z) / g_flWaterDepth);

#if (F_FORCE_FOG_OVERRIDE == 1)
    depthFactor = depthFactor * (g_flForceFogOverrideMax - g_flForceFogOverrideMin) + g_flForceFogOverrideMin;
#endif

    return depthFactor;
}

vec4 ShadeFullWater(vec3 normal, vec3 cameraRay)
{
    float cameraDistance = length(cameraRay);
    vec3 viewDirection = cameraRay / cameraDistance;

    vec2 screenUv = gl_FragCoord.xy * g_vInvViewportSize;
    float depthFactor = GetWaterDepthFactor(cameraRay);

    vec2 refractionUv = screenUv - normal.xy * (g_flRefractionAmount * depthFactor) * ScreenOffsetScale / cameraDistance;
    vec3 refracted = textureLod(g_tSceneColor, saturate(refractionUv), 0.0).rgb;

    // The light on an upward facing surface, with the sun reaching it and with the sun fully shadowed
    float sunFacing = saturate(dot(Up, GetSunDirection()));
    vec3 directionalAmbient = g_vAmbientDirectionColor.rgb * saturate(dot(Up, g_vAmbientDirection_ShadowAmount.xyz));
    vec3 skyAmbient = GetSkyAmbientLighting(Up);

    vec3 litIllumination = GetSunColor() * sunFacing + directionalAmbient + skyAmbient * (1.0 - sunFacing);
    vec3 shadowedIllumination = directionalAmbient * (1.0 - g_vAmbientDirection_ShadowAmount.w) + skyAmbient;
    vec3 illumination = mix(litIllumination, shadowedIllumination, g_flWaterFogShadowFactor);
    vec3 tinted = mix(refracted, refracted * g_vRefractionTint, saturate(3.5 * depthFactor));
    vec3 color = mix(tinted, g_vWaterFogColor * illumination, depthFactor);

    // TODO: add the planar reflection map, g_tReflectionMap times g_vReflectionTint, scaled by
    // g_flReflectance * saturate((depthFactor - 0.05) * 20) * saturate(2 * depthFactor)

    float highlight = pow(saturate(dot(g_vReflectionDir, reflect(-viewDirection, normal))), g_flReflectionPower);
    vec3 specularColor = g_vSpecularAlbedoColor.rgb * litIllumination + g_vSpecularLitColor.rgb;
    color += g_vReflectionColor * specularColor * highlight;

    return vec4(saturate(color), 1.0);
}

#endif

void main()
{
#if (F_FORCE_NULL_WATER == 1)
    discard;
#else
    vec3 normal = GetSurfaceNormal();
    vec3 cameraRay = vFragPosition - g_vCameraPositionWs;

    #if (F_FORCE_CHEAP_WATER == 1)
        outputColor = ShadeCheapWater(normal, normalize(cameraRay));
    #else
        outputColor = ShadeFullWater(normal, cameraRay);
    #endif
#endif
}
