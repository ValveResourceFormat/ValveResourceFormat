#version 460

#ifndef VRF_DOTA_GLOBAL_LIGHT_INCLUDE
#define VRF_DOTA_GLOBAL_LIGHT_INCLUDE

// The normal map applied on the interpolated vertex frame, which is normalized per fragment.
vec3 CalculateNormalizedWorldNormal(vec3 normalTs)
{
    vec3 normal = normalize(vNormalOut);
    vec3 tangent = normalize(vTangentOut.xyz);
    vec3 bitangent = normalize(GetBitangent(vNormalOut, vTangentOut));

    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    return normalize(tangentSpace * normalTs);
}

void ApplyAlphaTest(float alpha, float reference)
{
#if F_ALPHA_TEST == 1
    if (alpha < reference)
    {
        discard;
    }
#endif
}

// The ambient light of the entity's ambient direction, unaffected by shadows.
vec3 CalculateDotaDirectionalAmbient(vec3 worldNormal)
{
    float facing = saturate(dot(worldNormal, g_vAmbientDirection_ShadowAmount.xyz));
    return g_vAmbientDirectionColor.rgb * facing;
}

// The light that falls on an albedo: the sun where it reaches, the directional ambient, and the sky and
// ground colors fading in where the sun does not. The sun shadow darkens the directional ambient only by
// the shadow amount of the ambient direction.
// sunLit is the sun shadow times the lambert facing of the surface.
// Needs lighting_common.slang and shadowmapping.slang.
// TODO: the screen space ambient occlusion scales the sun by 1 - 0.35 * ao and the two ambient terms by (1 - ao)^2.
vec3 CalculateDotaGlobalLight(vec3 worldNormal, vec3 fragPosition, out float sunLit)
{
    float sunShadow = CalculateSunShadowMapVisibility(fragPosition);
    sunLit = saturate(dot(worldNormal, GetSunDirection())) * sunShadow;

    vec3 sun = GetSunColor() * sunLit;

    float ambientShadow = mix(1.0, sunShadow, g_vAmbientDirection_ShadowAmount.w);
    vec3 directionalAmbient = CalculateDotaDirectionalAmbient(worldNormal) * ambientShadow;

    vec3 shadowFill = GetSkyAmbientLighting(worldNormal) * (1.0 - sunLit);

    return sun + directionalAmbient + shadowFill;
}

// The Phong highlight of the entity's specular light, a direction of its own rather than the sun's.
// Scaled by the reflectance, it is added as g_vSpecularAlbedoColor to the albedo before lighting, and as
// g_vSpecularLitColor after lighting, scaled by the lit sun term.
float CalculateDotaSpecular(vec3 reflectionDir)
{
    float facing = saturate(dot(g_vSpecularDirection_Power.xyz, reflectionDir));

    // pow(0, 0) is undefined, and the exponent is 0 without a light info
    return pow(facing, max(g_vSpecularDirection_Power.w, 0.0001));
}

float CalculateDotaSpecular(vec3 worldNormal, vec3 fragPosition)
{
    vec3 toCamera = normalize(g_vCameraPositionWs - fragPosition);
    return CalculateDotaSpecular(reflect(toCamera, worldNormal));
}

// Blends tintC and tintD into a tint by a mask that picks tintC above 0.5 and tintD below
vec3 ApplyTintMask2(vec3 tint, float mask, vec3 tintC, vec3 tintD)
{
    float amountC = saturate((mask - 0.5) * 2.0);
    float amountD = saturate((0.5 - mask) * 2.0);
    return mix(tint, tintC * amountC + tintD * amountD, amountC + amountD);
}

#endif
