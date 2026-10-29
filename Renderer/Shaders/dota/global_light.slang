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

// The light that falls on an albedo: the sun where it reaches, the directional ambient that the sun shadow
// only darkens by its shadow amount, and the sky and ground colors fading in where the sun does not.
// sunShadow is the sun shadow alone, without the facing of the surface.
// Needs lighting_common.slang and shadowmapping.slang.
// TODO: the screen space ambient occlusion scales the sun by 1 - 0.35 * ao and the two ambient terms by (1 - ao)^2.
vec3 CalculateDotaGlobalLight(vec3 worldNormal, vec3 fragPosition, out float sunShadow)
{
    sunShadow = CalculateSunShadowMapVisibility(fragPosition);
    float sunLit = saturate(dot(worldNormal, GetSunDirection())) * sunShadow;

    vec3 sun = GetSunColor() * sunLit;

    float ambientShadow = mix(1.0, sunShadow, g_vAmbientDirection_ShadowAmount.w);
    vec3 directionalAmbient = CalculateDotaDirectionalAmbient(worldNormal) * ambientShadow;

    vec3 shadowFill = GetSkyAmbientLighting(worldNormal) * (1.0 - sunLit);

    return sun + directionalAmbient + shadowFill;
}

// The Phong highlight of the entity's specular light, a direction of its own rather than the sun's.
// Scaled by the reflectance, it is added as g_vSpecularAlbedoColor to the albedo before lighting, and as
// g_vSpecularLitColor after lighting where the sun shadow does not fall, whichever way the surface faces.
float CalculateDotaSpecular(vec3 worldNormal, vec3 fragPosition)
{
    vec3 toCamera = normalize(g_vCameraPositionWs - fragPosition);
    float facing = saturate(dot(g_vSpecularDirection_Power.xyz, reflect(toCamera, worldNormal)));

    return pow(facing, g_vSpecularDirection_Power.w);
}

#endif
