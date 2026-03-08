#version 460

#ifndef VRF_LIGHTING_BARN_INCLUDE
#define VRF_LIGHTING_BARN_INCLUDE

struct BarnLightConstants
{
    mat4 m_mBarnFrustum;
    vec4 m_vBarnLightPosition;
    vec4 m_vBarnLightDistanceFade_vSkirt;
    vec4 m_vBarnLightColor_flCookie;
    vec4 m_vBarnLightOrientationQ;
    vec3 m_vBarnLightAngleFade;
    vec4 m_vBarnLightShadowOffsetScale;
    vec4 m_vBarnLightCookieParameters;
    vec4 m_vBarnLightBakedShadowMask;
    float m_fBarnLightMinRoughness;
    float m_fBarnLightShadowScale;
    // PathTraceIndex (short) is not used
    uint m_nPathTraceIndex_BarnLightFlags;
    mat3x4 m_mBarnIlluminationFromWorld;
};

layout(std430, binding = 12) readonly buffer BarnLightBuffer {
    BarnLightConstants g_BarnLights[];
};

// Game uses Texture3D
uniform sampler2DArray g_tLightCookieTexture; // ClampToBorder
uniform sampler2DArray g_tLightCookieTextureWrap; // Wrap
// We currently have a separate shadow map for all barn lights
uniform layout(binding = 14) sampler2DShadow g_tBarnLightShadowDepth;

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
    // No tile culling
    for (uint i = 0; i < g_nNumBarnLights; i++)
    {
        BarnLightConstants barnLight = g_BarnLights[i];

        float visibility = saturate(1.0 - dot(bakedShadow, barnLight.m_vBarnLightBakedShadowMask));
        if (visibility == 0.0)
        {
            // lighting.DiffuseDirect *= vec3(1.0, 0.0, 0.0);
            continue;
        }

        vec4 clipPos = barnLight.m_mBarnFrustum * vec4(mat.PositionWS, 1.0);
        vec3 ndc = clipPos.xyz / clipPos.w;
        if (any(lessThanEqual(ndc, vec3(-1.0, -1.0, 0.0))) || any(greaterThanEqual(ndc, vec3(1.0))))
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
            skirtFade = smoothstep(0.0, 1.0, ndc.z * barnLight.m_vBarnLightDistanceFade_vSkirt.z);
        }
        if (barnLight.m_vBarnLightDistanceFade_vSkirt.w > 0.0)
        {
            skirtFade *= smoothstep(0.0, 1.0, (1.0 - ndc.z) * barnLight.m_vBarnLightDistanceFade_vSkirt.w);
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

            float invSqAtten = barnLight.m_vBarnLightPosition.w / max(dist * dist, barnLight.m_vBarnLightPosition.w);
            float distFade = smoothstep(0.0, 1.0, barnLight.m_vBarnLightDistanceFade_vSkirt.x + barnLight.m_vBarnLightDistanceFade_vSkirt.y * dist);
            attenuation = skirtFade * invSqAtten * distFade;

            lightDir = normalize(toLight);

            vec3 closestDir = toLight;
            if (barnLight.m_vBarnLightAngleFade.z > 0.0)
            {
                vec3 segment = QuaternionRotation(vec3(0, 1, 0), barnLight.m_vBarnLightOrientationQ) * barnLight.m_vBarnLightAngleFade.z;
                vec3 segStart = toLight - segment;
                vec3 segDir = 2.0 * segment;

                float proj = dot(-segStart, segDir);
                closestDir = proj <= 0.0
                    ? segStart
                    : segStart + segDir * min(1.0, proj / dot(segDir, segDir));
            }

            vec3 entityForward = QuaternionRotation(vec3(1, 0, 0), barnLight.m_vBarnLightOrientationQ);
            attenuation *= clamp(barnLight.m_vBarnLightAngleFade.x + barnLight.m_vBarnLightAngleFade.y * dot(entityForward, normalize(closestDir)), 0.0, 1.0);
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
            vec3 cookieColor = texture(g_tLightCookieTexture, vec3(cookieUV, layer)).rgb;
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
            vec3 cookieColor = texture(g_tLightCookieTextureWrap, vec3(cookieUV, layer)).rgb;
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
                    * pow(max(pow(barnLight.m_vBarnLightCookieParameters.y * absNdc.x, p) + pow(barnLight.m_vBarnLightCookieParameters.x * absNdc.y, p), 1.175e-38), np);
                float outerDist = pow(max(pow(absNdc.x, p) + pow(absNdc.y, p), 1.175e-38), np);

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

            // Custom fix for omni face seams
            shadowUV *= 0.995;

            // This is only used because of no g_vShadow3x3PCFTermC0-3
            vec2 texelSize = 1.0 / vec2(textureSize(g_tBarnLightShadowDepth, 0));
            shadowUV = shadowUV * barnLight.m_vBarnLightShadowOffsetScale.zw
                     + barnLight.m_vBarnLightShadowOffsetScale.xy;

            const float g_flShadowCascadeReceiverDepthBias = -0.000015;
            ndc.z += g_flShadowCascadeReceiverDepthBias;

            float shadowRef = saturate(ndc.z + g_flShadowCascadeReceiverDepthBias);

            //float shadow = ComputeBarnLightShadow(shadowUV, shadowRef);
            float shadow = ComputeBarnLightShadow_PCF_3x3_Gaussian(shadowUV, shadowRef, texelSize);

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

#endif
