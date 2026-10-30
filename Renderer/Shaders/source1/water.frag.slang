#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "../common/lighting_constants.slang"
#include "../common/depth.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tNormal;
uniform sampler2D g_tSceneColor; // reserved

uniform int F_BUMPMAP;
uniform int F_REFRACT;
uniform vec3 g_vWaterFogColor = vec3(0.1, 0.2, 0.25); // SrgbRead(true)
uniform vec3 g_vRefractTint = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vReflectTint = vec3(1.0); // SrgbRead(true)
uniform float g_flRefractAmount = 0.0;
uniform float g_flWaterFogStart = 0.0;
uniform float g_flWaterFogEnd = 0.0;
uniform float g_flCheapWaterStartDistance = 500.0;
uniform float g_flCheapWaterEndDistance = 1000.0;

// How far the scene at a screen position is behind the water surface along the view ray, negative when it is in front
float GetDistanceBehindSurface(vec2 screenCoord, vec3 cameraRay)
{
    float sceneDepth = textureLod(g_tSceneDepth, screenCoord, 0.0).x;
    vec3 scenePosition = GetWorldPositionFromDepthValue(sceneDepth, cameraRay);

    return distance(scenePosition, g_vCameraPositionWs) - length(cameraRay);
}

// How much of the water fog is between the surface and what is seen through it, from 0 to 1
float GetWaterFogFactor(float distanceInWater)
{
    return saturate((distanceInWater - g_flWaterFogStart) / max(g_flWaterFogEnd - g_flWaterFogStart, 1.0));
}

void main()
{
    vec3 geometricNormal = normalize(cross(dFdx(vFragPosition), dFdy(vFragPosition)));
    vec3 cameraRay = vFragPosition - g_vCameraPositionWs;
    vec3 eyeDirection = normalize(-cameraRay);

    // Seen from below, the surface faces the camera
    geometricNormal *= sign(dot(geometricNormal, eyeDirection));

    vec3 tangentNormal = vec3(0.0, 0.0, 1.0);
    float normalAlpha = 1.0;
    vec3 normal = geometricNormal;

    if (F_BUMPMAP != 0)
    {
        vec4 normalTexel = texture(g_tNormal, vTexCoordOut);
        tangentNormal = normalTexel.xyz * 2.0 - 1.0;
        normalAlpha = normalTexel.a;
        mat3 tangentFrame = GetSource1TangentFrame(geometricNormal, vec4(0.0), vFragPosition, vTexCoordOut);
        normal = normalize(tangentFrame * tangentNormal);
    }

    vec3 reflection;

    if (F_ENVMAP != 0)
    {
        reflection = texture(g_tEnvMap, reflect(-eyeDirection, normal)).rgb;
    }
    else
    {
        reflection = max(GetSkyAmbientLighting(reflect(-eyeDirection, normal)), vec3(0.0));
    }

    reflection *= g_vReflectTint;

    float facing = saturate(dot(normal, eyeDirection));
    float fresnel = pow(1.0 - facing, 5.0);
    float distanceReflection = saturate((length(cameraRay) - g_flCheapWaterStartDistance) / max(g_flCheapWaterEndDistance - g_flCheapWaterStartDistance, 1.0));
    float reflectionAmount = saturate(fresnel + distanceReflection);

    if (F_REFRACT == 0)
    {
        vec3 color = mix(g_vWaterFogColor, reflection, fresnel);
        ApplyFog(color, vFragPosition);
        outputColor = vec4(color, g_flOpacityScale);
        return;
    }

    vec2 screenCoord = gl_FragCoord.xy / vec2(textureSize(g_tSceneColor, 0));
    float fogFactor = GetWaterFogFactor(GetDistanceBehindSurface(screenCoord, cameraRay));

    // The refraction is offset by the ripples, less where the water is shallow. The screen coordinate runs upward.
    vec2 refractOffset = vec2(tangentNormal.x, -tangentNormal.y) * normalAlpha * g_flRefractAmount * fogFactor;
    vec2 refractCoord = saturate(screenCoord + refractOffset);

    // An offset that lands on something above the water would show it through the surface
    if (GetDistanceBehindSurface(refractCoord, cameraRay) < 0.0)
    {
        refractCoord = screenCoord;
    }

    vec3 refraction = textureLod(g_tSceneColor, refractCoord, 0.0).rgb * g_vRefractTint;
    refraction = mix(refraction, g_vWaterFogColor, saturate(fogFactor - 0.05));

    // Shallow water fades into the shore without a reflection
    reflectionAmount *= saturate((fogFactor - 0.05) * 20.0);

    vec3 color = mix(refraction, reflection, reflectionAmount);
    ApplyFog(color, vFragPosition);
    outputColor = vec4(color, 1.0);
}
