#version 460

#ifndef VRF_LIGHTING_BARN_INCLUDE
#define VRF_LIGHTING_BARN_INCLUDE

#include "light_cull.slang"
#include "lighting_common.slang"
#include "shadow_pcf.slang"

// A light that is not a point never shines sharper than a roughness of its own
void ApplyMinRoughness(inout MaterialProperties_t mat, float minRoughness)
{
    mat.IsometricRoughness = max(mat.IsometricRoughness, minRoughness);
    mat.Roughness = max(mat.Roughness, vec2(minRoughness));
}

struct BarnLightConstants
{
    mat4 m_mBarnFrustum;
    vec4 m_vBarnLightPosition;
    vec4 m_vBarnLightDistanceFade_vSkirt;
    vec4 m_vBarnLightColor_flCookie;
    vec4 m_vBarnLightOrientationQ;
    vec3 m_vBarnLightAngleFade;
    float m_flBarnLightLuminaireHalfWidth;
    vec4 m_vBarnLightShadowOffsetScale;
    vec4 m_vBarnLightCookieParameters;
    vec4 m_vBarnLightBakedShadowMask;
    float m_fBarnLightMinRoughness;
    float m_fBarnLightShadowScale;
    uint m_nPathTraceIndex_BarnLightFlags;
    float m_flBarnLightLuminaireHalfHeight;
    mat3x4 m_mBarnIlluminationFromWorld;
};

layout(std430, binding = 3) readonly buffer BarnLightBuffer {
    BarnLightConstants g_BarnLights[];
};

uint g_nBarnLightsIterated = 0u;

const uint BARN_LIGHT_FLAG_LIT_INSIDE_NEAR_PLANE = 0x8000u;
const uint BARN_LIGHT_FLAG_RECT_LUMINAIRE = 0x1000u;
const uint BARN_LIGHT_FLAG_DISC_LUMINAIRE = 0x2000u;
const uint BARN_LIGHT_FLAG_SWAPPED_SHADOW_AXES = 0x4u; // The shadow map is read with the frustum's x and y swapped

// The other shaders light rect lights from their centre, while Desk Job (1) and Deadlock (2) shade them as
// area lights, Deadlock keeping the angle fade on them
#define BARN_LIGHT_AREA_LIGHTS_DESK_JOB 1
#define BARN_LIGHT_AREA_LIGHTS_DEADLOCK 2

#ifndef BARN_LIGHT_AREA_LIGHTS
#define BARN_LIGHT_AREA_LIGHTS 0
#endif

// Stands in for the area light integral by lighting from the luminaire's closest point: it matches the
// integral far from the luminaire and is capped by the luminaire's area close to it.
float ComputeBarnAreaLightAttenuation(BarnLightConstants barnLight, vec3 forward, vec3 toCenter, out vec3 lightDir)
{
    vec3 axisY = QuaternionRotation(vec3(0, 1, 0), barnLight.m_vBarnLightOrientationQ);
    vec3 axisZ = QuaternionRotation(vec3(0, 0, 1), barnLight.m_vBarnLightOrientationQ);

    vec2 halfSize = max(vec2(barnLight.m_flBarnLightLuminaireHalfWidth, barnLight.m_flBarnLightLuminaireHalfHeight), vec2(0.001));
    vec2 local = vec2(dot(-toCenter, axisY), dot(-toCenter, axisZ));
    vec2 closest;
    float area;

    if ((barnLight.m_nPathTraceIndex_BarnLightFlags & BARN_LIGHT_FLAG_DISC_LUMINAIRE) != 0u)
    {
        vec2 unit = local / halfSize;
        closest = (dot(unit, unit) > 1.0 ? normalize(unit) : unit) * halfSize;
        area = PI * halfSize.x * halfSize.y;
    }
    else
    {
        closest = clamp(local, -halfSize, halfSize);
        area = 4.0 * halfSize.x * halfSize.y;
    }

    vec3 toLight = toCenter + axisY * closest.x + axisZ * closest.y;

    float distSq = max(length_squared(toLight), 0.0001);
    lightDir = toLight * inversesqrt(distSq);
    float cosEmit = saturate(-dot(forward, lightDir));
    return cosEmit / max(distSq, area / PI);
}

float BarnLightFade(float x)
{
    return smoothstep(0.0, 1.0, x);
}

// Sampled as 2D arrays
uniform sampler2DArray g_tLightCookieTexture; // ClampToBorder, reserved
uniform sampler2DArray g_tLightCookieTextureWrap; // Wrap, reserved

float GetBarnLightVisibility(BarnLightConstants barnLight, vec4 bakedShadow)
{
    float visibility = saturate(1.0 - dot(bakedShadow, barnLight.m_vBarnLightBakedShadowMask));

#if BARN_LIGHT_AREA_LIGHTS == BARN_LIGHT_AREA_LIGHTS_DEADLOCK
    // Deadlock applies the baked visibility twice
    visibility *= visibility;
#endif

    return visibility;
}

// Whether the position is inside the light's frustum and volume, with its position in the frustum
bool GetBarnLightNdc(BarnLightConstants barnLight, vec3 positionWS, out vec3 ndc)
{
    vec4 clipPos = barnLight.m_mBarnFrustum * vec4(positionWS, 1.0);
    ndc = clipPos.xyz / clipPos.w;

    bool bLitInsideNearPlane = (barnLight.m_nPathTraceIndex_BarnLightFlags & BARN_LIGHT_FLAG_LIT_INSIDE_NEAR_PLANE) != 0u;
    bool bBeforeNearPlane = bLitInsideNearPlane ? clipPos.w <= 0.0001 : ndc.z <= 0.0;

    if (bBeforeNearPlane || any(lessThanEqual(ndc.xy, vec2(-1.0))) || any(greaterThanEqual(ndc, vec3(1.0))))
    {
        return false;
    }

    vec3 obbPos = vec4(positionWS, 1.0) * barnLight.m_mBarnIlluminationFromWorld;
    return !any(greaterThan(abs(obbPos), vec3(1.0)));
}

float GetBarnLightSkirtFade(BarnLightConstants barnLight, float ndcZ)
{
    float skirtFade = 1.0;

    if (barnLight.m_vBarnLightDistanceFade_vSkirt.z > 0.0)
    {
        skirtFade = BarnLightFade(ndcZ * barnLight.m_vBarnLightDistanceFade_vSkirt.z);
    }

    if (barnLight.m_vBarnLightDistanceFade_vSkirt.w > 0.0)
    {
        skirtFade *= BarnLightFade((1.0 - ndcZ) * barnLight.m_vBarnLightDistanceFade_vSkirt.w);
    }

    return skirtFade;
}

bool IsBarnAreaLight(BarnLightConstants barnLight)
{
#if BARN_LIGHT_AREA_LIGHTS
    return (barnLight.m_nPathTraceIndex_BarnLightFlags & (BARN_LIGHT_FLAG_RECT_LUMINAIRE | BARN_LIGHT_FLAG_DISC_LUMINAIRE)) != 0u;
#else
    return false;
#endif
}

float GetBarnAreaLightAngleFade(float angleFade)
{
#if BARN_LIGHT_AREA_LIGHTS == BARN_LIGHT_AREA_LIGHTS_DESK_JOB
    return 1.0;
#else
    return angleFade;
#endif
}

// The fade by the angle between the light's forward axis and the direction to the light, or to the nearest point of its segment
float GetBarnLightAngleFade(BarnLightConstants barnLight, vec3 toLight, vec3 entityForward)
{
    vec3 closestDir = toLight;

    if (barnLight.m_vBarnLightAngleFade.z > 0.0)
    {
        vec3 segment = QuaternionRotation(vec3(0, 1, 0), barnLight.m_vBarnLightOrientationQ) * barnLight.m_vBarnLightAngleFade.z;
        vec3 segStart = toLight - segment;
        vec3 segDir = 2.0 * segment;

        float proj = dot(-segStart, segDir);
        closestDir = proj <= 0.0
            ? segStart
            : segStart + segDir * min(1.0, proj / length_squared(segDir));
    }

    return saturate(barnLight.m_vBarnLightAngleFade.x + barnLight.m_vBarnLightAngleFade.y * dot(entityForward, normalize(closestDir)));
}

float GetBarnLightAttenuation(BarnLightConstants barnLight, vec3 positionWS, float skirtFade, out vec3 lightDir)
{
    if (barnLight.m_vBarnLightPosition.w == 0.0)
    {
        lightDir = barnLight.m_vBarnLightPosition.xyz;
        return skirtFade;
    }

    vec3 toLight = barnLight.m_vBarnLightPosition.xyz - positionWS;
    float dist = length(toLight);
    float distFade = BarnLightFade(barnLight.m_vBarnLightDistanceFade_vSkirt.x + barnLight.m_vBarnLightDistanceFade_vSkirt.y * dist);

    vec3 entityForward = QuaternionRotation(vec3(1, 0, 0), barnLight.m_vBarnLightOrientationQ);
    float angleFade = GetBarnLightAngleFade(barnLight, toLight, entityForward);
    float attenuation;

    if (IsBarnAreaLight(barnLight))
    {
        attenuation = skirtFade * distFade * ComputeBarnAreaLightAttenuation(barnLight, entityForward, toLight, lightDir);
        angleFade = GetBarnAreaLightAngleFade(angleFade);
    }
    else
    {
        float invSqAtten = barnLight.m_vBarnLightPosition.w / max(dist * dist, barnLight.m_vBarnLightPosition.w);
        attenuation = skirtFade * invSqAtten * distFade;
        lightDir = toLight / dist;
    }

    return attenuation * angleFade;
}

// Fades the light out towards the edges of its frustum
float GetBarnLightSoftEdge(BarnLightConstants barnLight, vec2 ndcXY)
{
    vec2 absNdc = abs(ndcXY);
    float roundness = barnLight.m_vBarnLightCookieParameters.z;

    if (roundness == 0.0)
    {
        float fadeX = (barnLight.m_vBarnLightCookieParameters.x < 1.0)
            ? smoothstep(1.0, barnLight.m_vBarnLightCookieParameters.x, absNdc.x)
            : 1.0;
        float fadeY = (barnLight.m_vBarnLightCookieParameters.y < 1.0)
            ? smoothstep(1.0, barnLight.m_vBarnLightCookieParameters.y, absNdc.y)
            : 1.0;
        return fadeX * fadeY;
    }

    float p = 2.0 / roundness;
    float np = -0.5 * roundness;
    float innerDist = (barnLight.m_vBarnLightCookieParameters.x * barnLight.m_vBarnLightCookieParameters.y)
        * pow(max(SafePow(barnLight.m_vBarnLightCookieParameters.y * absNdc.x, p) + SafePow(barnLight.m_vBarnLightCookieParameters.x * absNdc.y, p), 1.175e-38), np);
    float outerDist = pow(max(SafePow(absNdc.x, p) + SafePow(absNdc.y, p), 1.175e-38), np);

    return innerDist < outerDist ? smoothstep(outerDist, innerDist, 1.0) : float(outerDist > 1.0);
}

// The cookie's scale in zw and offset in xy
vec2 ApplyBarnCookieTransform(BarnLightConstants barnLight, vec2 cookieUV)
{
    return cookieUV * barnLight.m_vBarnLightCookieParameters.zw + barnLight.m_vBarnLightCookieParameters.xy;
}

// A positive cookie index projects a cookie through the frustum, a negative one wraps a cookie around the light.
// Without a cookie the light has a soft edge.
vec3 GetBarnLightCookie(BarnLightConstants barnLight, vec3 ndc, vec3 lightDir)
{
    if (barnLight.m_vBarnLightColor_flCookie.w > 0.0)
    {
        vec2 cookieUV = ndc.xy * vec2(-0.5, -0.5) + vec2(0.5);
        cookieUV = ApplyBarnCookieTransform(barnLight, cookieUV);
        float layer = barnLight.m_vBarnLightColor_flCookie.w - 1.0;
        return textureLod(g_tLightCookieTexture, vec3(cookieUV, layer), 0.0).rgb;
    }

    if (barnLight.m_vBarnLightColor_flCookie.w < 0.0)
    {
        vec3 rotated = QuaternionRotation(-lightDir, vec4(-barnLight.m_vBarnLightOrientationQ.xyz, barnLight.m_vBarnLightOrientationQ.w));
        vec2 cookieUV = vec2(
            atan(rotated.y, -rotated.x) / TAU,
            acos(rotated.z) / PI
        );
        cookieUV = ApplyBarnCookieTransform(barnLight, cookieUV);

        float layer = -barnLight.m_vBarnLightColor_flCookie.w - 1.0;
        return textureLod(g_tLightCookieTextureWrap, vec3(cookieUV, layer), 0.0).rgb;
    }

    return vec3(GetBarnLightSoftEdge(barnLight, ndc.xy));
}

float GetBarnLightShadow(BarnLightConstants barnLight, vec3 ndc)
{
    if (barnLight.m_vBarnLightShadowOffsetScale.z <= 0.0)
    {
        return 1.0;
    }

    vec2 shadowUV = ndc.xy;
    if ((barnLight.m_nPathTraceIndex_BarnLightFlags & BARN_LIGHT_FLAG_SWAPPED_SHADOW_AXES) != 0u)
    {
        shadowUV = ndc.yx * vec2(1.0, -1.0);
    }

    vec2 texelSize = 1.0 / vec2(textureSize(g_tBarnLightShadowDepth, 0));
    shadowUV = shadowUV * barnLight.m_vBarnLightShadowOffsetScale.zw
             + barnLight.m_vBarnLightShadowOffsetScale.xy;

    float shadowRef = saturate(ndc.z + SHADOW_RECEIVER_DEPTH_BIAS);

    float shadow = ComputeBarnLightShadow_PCF_3x3_Gaussian(shadowUV, shadowRef, texelSize);

    return mix(1.0, shadow, barnLight.m_fBarnLightShadowScale);
}

void CalculateBarnLighting(inout LightingTerms_t lighting, in MaterialProperties_t mat, vec4 bakedShadow)
{
    CullMasks_t masks = GetBarnLightCullMasks(mat.PositionWS);

    uint word = 0u;
    uint mask = 0u;

    while (g_nBarnLightsIterated < g_nNumBarnLights)
    {
        while (mask == 0u)
        {
            if (word >= masks.m_nWords)
            {
                return;
            }

            mask = GetCullMaskWord(masks, word, GetCullLiveMask(word, g_nNumBarnLights));
            word++;
        }

        uint i = uint(findLSB(mask)) + ((word - 1u) * 32u);
        mask &= mask - 1u;

        g_nBarnLightsIterated++;

        BarnLightConstants barnLight = g_BarnLights[i];

        float visibility = GetBarnLightVisibility(barnLight, bakedShadow);
        if (visibility == 0.0)
        {
            continue;
        }

        vec3 ndc;
        if (!GetBarnLightNdc(barnLight, mat.PositionWS, ndc))
        {
            continue;
        }

        float skirtFade = GetBarnLightSkirtFade(barnLight, ndc.z);
        if (skirtFade == 0.0)
        {
            continue;
        }

        vec3 lightDir;
        float attenuation = GetBarnLightAttenuation(barnLight, mat.PositionWS, skirtFade, lightDir);

        vec3 lightColor = barnLight.m_vBarnLightColor_flCookie.xyz
            * visibility
            * attenuation;

        lightColor *= GetBarnLightCookie(barnLight, ndc, lightDir);

        if (lightColor == vec3(0.0))
        {
            continue;
        }

        lightColor *= GetBarnLightShadow(barnLight, ndc);

        if (lightColor == vec3(0.0))
        {
            continue;
        }

        float savedIsoRoughness = mat.IsometricRoughness;
        vec2 savedRoughness = mat.Roughness;
        ApplyMinRoughness(mat, barnLight.m_fBarnLightMinRoughness);

        CalculateShading(lighting, lightDir, lightColor, mat);

        mat.IsometricRoughness = savedIsoRoughness;
        mat.Roughness = savedRoughness;
    }
}

#endif
