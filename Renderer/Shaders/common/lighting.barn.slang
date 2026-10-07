#version 460

#ifndef VRF_LIGHTING_BARN_INCLUDE
#define VRF_LIGHTING_BARN_INCLUDE

#include "light_cull.slang"

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
    // PathTraceIndex (short) is not used
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

// The CS2 world shaders fade linearly and take one shadow tap, its model shaders and other games
// use smoothstep fades and a 3x3 PCF. Shaders that stand in for CS2 world shaders define this first.
#ifndef BARN_LIGHT_CHEAP_WORLD_SHADING
#define BARN_LIGHT_CHEAP_WORLD_SHADING 0
#endif

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
#if BARN_LIGHT_CHEAP_WORLD_SHADING
    return saturate(x);
#else
    return smoothstep(0.0, 1.0, x);
#endif
}

// Game uses Texture3D
uniform sampler2DArray g_tLightCookieTexture; // ClampToBorder, reserved
uniform sampler2DArray g_tLightCookieTextureWrap; // Wrap, reserved

// We currently have a separate shadow map for all barn lights
uniform sampler2DShadow g_tBarnLightShadowDepth; // reserved

float ComputeBarnLightShadow(vec2 shadowUV, float shadowRef)
{
    return textureLod(g_tBarnLightShadowDepth, vec3(shadowUV, shadowRef), 0.0);
}

// from shadow in sbox
// game uses g_vShadow3x3PCFTermC0-3 instead of hardcoded weights
float ComputeBarnLightShadow_PCF_3x3_Gaussian(vec2 shadowUV, float shadowRef, vec2 texelSize)
{
    float shadow = dot(vec4(
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2( 1.0,  1.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2(-1.0,  1.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2( 1.0, -1.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2(-1.0, -1.0), shadowRef), 0.0)
    ), vec4(0.25));

    if (shadow == 0.0 || shadow == 1.0)
        return shadow;

    shadow *= 0.2996;
    shadow += dot(vec4(
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2( 1.0,  0.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2(-1.0,  0.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2( 0.0, -1.0), shadowRef), 0.0),
        textureLod(g_tBarnLightShadowDepth, vec3(shadowUV + texelSize * vec2( 0.0,  1.0), shadowRef), 0.0)
    ), vec4(0.1236));
    shadow += textureLod(g_tBarnLightShadowDepth, vec3(shadowUV, shadowRef), 0.0) * 0.206;

    return shadow;
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

        float visibility = saturate(1.0 - dot(bakedShadow, barnLight.m_vBarnLightBakedShadowMask));
        if (visibility == 0.0)
        {
            // lighting.DiffuseDirect *= vec3(1.0, 0.0, 0.0);
            continue;
        }

        vec4 clipPos = barnLight.m_mBarnFrustum * vec4(mat.PositionWS, 1.0);
        vec3 ndc = clipPos.xyz / clipPos.w;

        bool bLitInsideNearPlane = (barnLight.m_nPathTraceIndex_BarnLightFlags & BARN_LIGHT_FLAG_LIT_INSIDE_NEAR_PLANE) != 0u;
        bool bBeforeNearPlane = bLitInsideNearPlane ? clipPos.w <= 0.0001 : ndc.z <= 0.0;

        if (bBeforeNearPlane || any(lessThanEqual(ndc.xy, vec2(-1.0))) || any(greaterThanEqual(ndc, vec3(1.0))))
        {
            continue;
        }

        vec3 obbPos = vec4(mat.PositionWS, 1.0) * barnLight.m_mBarnIlluminationFromWorld;
        if (any(greaterThan(abs(obbPos), vec3(1.0))))
        {
            continue;
        }

        float skirtFade = 1.0;
        if (barnLight.m_vBarnLightDistanceFade_vSkirt.z > 0.0)
        {
            skirtFade = BarnLightFade(ndc.z * barnLight.m_vBarnLightDistanceFade_vSkirt.z);
        }
        if (barnLight.m_vBarnLightDistanceFade_vSkirt.w > 0.0)
        {
            skirtFade *= BarnLightFade((1.0 - ndc.z) * barnLight.m_vBarnLightDistanceFade_vSkirt.w);
        }

        if (skirtFade == 0.0)
        {
            continue;
        }

        vec3 lightDir;
        float attenuation;
        if (barnLight.m_vBarnLightPosition.w != 0.0)
        {
            vec3 toLight = barnLight.m_vBarnLightPosition.xyz - mat.PositionWS;
            float dist = length(toLight);
            float distFade = BarnLightFade(barnLight.m_vBarnLightDistanceFade_vSkirt.x + barnLight.m_vBarnLightDistanceFade_vSkirt.y * dist);

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

            vec3 entityForward = QuaternionRotation(vec3(1, 0, 0), barnLight.m_vBarnLightOrientationQ);
            float angleFade = saturate(barnLight.m_vBarnLightAngleFade.x + barnLight.m_vBarnLightAngleFade.y * dot(entityForward, normalize(closestDir)));

#if BARN_LIGHT_AREA_LIGHTS
            if ((barnLight.m_nPathTraceIndex_BarnLightFlags & (BARN_LIGHT_FLAG_RECT_LUMINAIRE | BARN_LIGHT_FLAG_DISC_LUMINAIRE)) != 0u)
            {
                attenuation = skirtFade * distFade * ComputeBarnAreaLightAttenuation(barnLight, entityForward, toLight, lightDir);
#if BARN_LIGHT_AREA_LIGHTS == BARN_LIGHT_AREA_LIGHTS_DESK_JOB
                angleFade = 1.0;
#endif
            }
            else
#endif
            {
                float invSqAtten = barnLight.m_vBarnLightPosition.w / max(dist * dist, barnLight.m_vBarnLightPosition.w);
                attenuation = skirtFade * invSqAtten * distFade;
                lightDir = toLight / dist;
            }

            attenuation *= angleFade;
        }
        else
        {
            lightDir = barnLight.m_vBarnLightPosition.xyz;
            attenuation = skirtFade;
        }

        vec3 lightColor = barnLight.m_vBarnLightColor_flCookie.xyz
            * visibility
            * attenuation;

        if (barnLight.m_vBarnLightColor_flCookie.w > 0.0)
        {
            vec2 cookieUV = ndc.xy * vec2(-0.5, -0.5) + vec2(0.5);
            cookieUV = cookieUV * barnLight.m_vBarnLightCookieParameters.zw + barnLight.m_vBarnLightCookieParameters.xy;
            float layer = barnLight.m_vBarnLightColor_flCookie.w - 1.0;
            vec3 cookieColor = textureLod(g_tLightCookieTexture, vec3(cookieUV, layer), 0.0).rgb;
            lightColor *= cookieColor;
        }
        else if (barnLight.m_vBarnLightColor_flCookie.w < 0.0)
        {
            vec3 rotated = QuaternionRotation(-lightDir, vec4(-barnLight.m_vBarnLightOrientationQ.xyz, barnLight.m_vBarnLightOrientationQ.w));
            vec2 cookieUV = vec2(
                atan(rotated.y, -rotated.x) / TAU,
                acos(rotated.z) / PI
            );
            cookieUV = cookieUV * barnLight.m_vBarnLightCookieParameters.zw + barnLight.m_vBarnLightCookieParameters.xy;

            float layer = -barnLight.m_vBarnLightColor_flCookie.w - 1.0;
            vec3 cookieColor = textureLod(g_tLightCookieTextureWrap, vec3(cookieUV, layer), 0.0).rgb;
            lightColor *= cookieColor;
        }
        else
        {
            float softEdge;
            vec2 absNdc = abs(ndc.xy);
            float roundness = barnLight.m_vBarnLightCookieParameters.z;

            if (roundness == 0.0)
            {
                float fadeX = (barnLight.m_vBarnLightCookieParameters.x < 1.0)
                    ? smoothstep(1.0, barnLight.m_vBarnLightCookieParameters.x, absNdc.x)
                    : 1.0;
                float fadeY = (barnLight.m_vBarnLightCookieParameters.y < 1.0)
                    ? smoothstep(1.0, barnLight.m_vBarnLightCookieParameters.y, absNdc.y)
                    : 1.0;
                softEdge = fadeX * fadeY;
            }
            else
            {
                float p = 2.0 / roundness;
                float np = -0.5 * roundness;
                float innerDist = (barnLight.m_vBarnLightCookieParameters.x * barnLight.m_vBarnLightCookieParameters.y)
                    * pow(max(SafePow(barnLight.m_vBarnLightCookieParameters.y * absNdc.x, p) + SafePow(barnLight.m_vBarnLightCookieParameters.x * absNdc.y, p), 1.175e-38), np);
                float outerDist = pow(max(SafePow(absNdc.x, p) + SafePow(absNdc.y, p), 1.175e-38), np);

                softEdge = innerDist < outerDist ? smoothstep(outerDist, innerDist, 1.0) : float(outerDist > 1.0);
            }

            lightColor *= softEdge;
        }

        if (lightColor == vec3(0.0))
        {
            continue;
        }

        // Shadow map
        if (barnLight.m_vBarnLightShadowOffsetScale.z > 0.0 /* && !g_bOtherEnabled2.x */)
        {
            vec2 shadowUV = ndc.xy;
            if ((barnLight.m_nPathTraceIndex_BarnLightFlags & 4u) != 0u)
            {
                shadowUV = ndc.yx * vec2(1.0, -1.0);
            }

            // This is only used because of no g_vShadow3x3PCFTermC0-3
            vec2 texelSize = 1.0 / vec2(textureSize(g_tBarnLightShadowDepth, 0));
            shadowUV = shadowUV * barnLight.m_vBarnLightShadowOffsetScale.zw
                     + barnLight.m_vBarnLightShadowOffsetScale.xy;

            const float g_flShadowCascadeReceiverDepthBias = -0.000015;
            float shadowRef = saturate(ndc.z + g_flShadowCascadeReceiverDepthBias);

#if BARN_LIGHT_CHEAP_WORLD_SHADING
            float shadow = ComputeBarnLightShadow(shadowUV, shadowRef);
#else
            float shadow = ComputeBarnLightShadow_PCF_3x3_Gaussian(shadowUV, shadowRef, texelSize);
#endif

            lightColor *= mix(1.0, shadow, barnLight.m_fBarnLightShadowScale);
        }

        if (lightColor == vec3(0.0))
        {
            continue;
        }

        float savedIsoRoughness = mat.IsometricRoughness;
        vec2 savedRoughness = mat.Roughness;
        mat.IsometricRoughness = max(mat.IsometricRoughness, barnLight.m_fBarnLightMinRoughness);
        mat.Roughness = max(mat.Roughness, vec2(barnLight.m_fBarnLightMinRoughness));

        CalculateShading(lighting, lightDir, lightColor, mat);

        mat.IsometricRoughness = savedIsoRoughness;
        mat.Roughness = savedRoughness;
    }
}

bool GetBarnLightTileHeatmap(out vec3 color)
{
    const float kBusyTile = 12.0;

    if (g_nBarnLightsIterated == 0u)
    {
        color = vec3(0.0);
        return false;
    }

    float t = saturate(float(g_nBarnLightsIterated) / kBusyTile) * 3.0;

    color = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0), saturate(t));
    color = mix(color, vec3(1.0, 1.0, 0.0), saturate(t - 1.0));
    color = mix(color, vec3(1.0, 0.0, 0.0), saturate(t - 2.0));

    return true;
}

#endif
