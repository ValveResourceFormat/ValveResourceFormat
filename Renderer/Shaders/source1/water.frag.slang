#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "common_rendermodes_surface_ps.slang"
#include "../common/lighting_constants.slang"
#include "../common/depth.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tSceneColor; // reserved
uniform sampler2D g_tFlowmap; // Sampler(UserConfig)
uniform sampler2D g_tFlowNoise; // Sampler(UserConfig)

uniform int F_BUMPMAP;
uniform int F_FLOWMAP;
uniform float g_flFlowWorldUvScale = 1.0;
uniform float g_flFlowNormalUvScale = 1.0;
uniform float g_flFlowBumpStrength = 1.0;
uniform float g_flFlowTimeInterval = 0.4;
uniform float g_flFlowUvScrollDistance = 0.2;
uniform float g_flFlowNoiseScale = 0.0002;
uniform float g_flFlowTimeScale = 1.0;
uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig)
uniform int F_COLOR_FLOW;
uniform float g_flColorFlowUvScale = 1.0;
uniform float g_flColorFlowTimeInterval = 1.0;
uniform float g_flColorFlowUvScrollDistance = 0.0;
uniform float g_flColorFlowLerpExp = 1.0;
uniform float g_flColorFlowDisplaceByNormalStrength = 0.0;
uniform int F_REFRACT;
uniform int F_NO_FRESNEL;
uniform vec3 g_vWaterFogColor = vec3(0.1, 0.2, 0.25);
uniform vec3 g_vRefractTint = vec3(1.0);
uniform vec4 g_vReflectTint = vec4(1.0);
uniform float g_flRefractAmount = 0.0;
uniform float g_flWaterFogStart = 0.0;
uniform float g_flWaterFogEnd = 0.0;
uniform float g_flCheapWaterStartDistance = 500.0;
uniform float g_flCheapWaterEndDistance = 1000.0;

// The tangent space normal of water that flows along a flow map. Two layers of the normal map are laid in world space and
// scroll along the flow for one interval, and the layers alternate, so each fades out where it restarts.
vec3 GetFlowTangentNormal(vec2 worldCoord, vec2 flowTexel, float noise, out float normalAlpha)
{
    vec2 flow = flowTexel * 2.0 - 1.0;
    vec2 scroll = flow * g_flFlowUvScrollDistance;
    vec2 normalCoord = worldCoord / g_flFlowNormalUvScale;

    float phase = (g_flTime * g_flFlowTimeScale + noise) / (2.0 * g_flFlowTimeInterval);
    vec2 offsetA;
    vec2 offsetB;
    float progressA;
    float progressB;
    float weightA;
    float weightB;
    GetFlowLayers(phase, offsetA, offsetB, progressA, progressB, weightA, weightB);

    vec2 coordA = normalCoord + offsetA + scroll * progressA;
    vec2 coordB = normalCoord + offsetB + scroll * progressB;

    vec4 texelA = texture(g_tNormal, coordA);
    vec4 texelB = texture(g_tNormal, coordB);
    vec2 blended = mix(texelB.xy, texelA.xy, weightA) * 2.0 - 1.0;
    normalAlpha = mix(texelB.a, texelA.a, weightA);

    float strength = (dot(flow, flow) + 0.1) * g_flFlowBumpStrength;
    vec2 ripple = blended * strength;

    return vec3(ripple, sqrt(saturate(1.0 - dot(ripple, ripple))));
}

// The base texture of water that flows along the flow map, as the surface color. It alternates two layers like the
// ripples do, is displaced by the ripples and the view direction, and fades with the alpha of the flow map. The
// alpha of the result is how much of the surface it covers.
vec4 GetFlowColor(vec2 worldCoord, vec4 flowTexel, float noise, vec3 eyeDirection, vec2 ripple, float normalAlpha)
{
    vec2 scroll = (flowTexel.xy * 2.0 - 1.0) * g_flColorFlowUvScrollDistance;
    vec2 displacement = (eyeDirection.xy - ripple) * normalAlpha * g_flColorFlowDisplaceByNormalStrength;
    vec2 colorCoord = worldCoord / g_flColorFlowUvScale + displacement;

    float phase = g_flTime / (2.0 * g_flColorFlowTimeInterval) + noise;
    vec2 offsetA;
    vec2 offsetB;
    float progressA;
    float progressB;
    float weightA;
    float weightB;
    GetFlowLayers(phase, offsetA, offsetB, progressA, progressB, weightA, weightB);

    vec2 coordA = colorCoord + offsetA + (progressA - 0.5) * scroll;
    vec2 coordB = colorCoord + offsetB + (progressB - 0.5) * scroll;

    // Each layer is faded out where it restarts
    weightA = pow(weightA, g_flColorFlowLerpExp);
    weightB = pow(weightB, g_flColorFlowLerpExp);
    vec4 color = texture(g_tColor, coordA) * weightA + texture(g_tColor, coordB) * weightB;

    return color * flowTexel.a;
}

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

// The render modes of the surface, which has no color texture so the water fog color stands for the albedo
void ApplyWaterRenderModes(vec3 geometricNormal, vec3 normal, vec3 tangentNormal, vec3 eyeDirection)
{
    if (g_iRenderMode == 0)
    {
        return;
    }

    vec3 tangent = GetSource1TangentFrame(geometricNormal, vec4(0.0), vFragPosition, vTexCoordOut)[0];

    ApplySource1RenderModes(outputColor, g_vWaterFogColor, vec4(1.0), normal, eyeDirection, g_tNormal, vTexCoordOut);
    ApplySource1NormalRenderModes(outputColor, geometricNormal, tangent, tangentNormal, normal);
    ApplySource1CubemapRenderMode(outputColor, normal, vFragPosition);
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
    vec3 unnormalizedNormal = geometricNormal;

    vec3 flowColor = vec3(0.0);
    float flowColorAmount = 0.0;
    float flowReflectionMask = 1.0;

    if (F_FLOWMAP != 0)
    {
        vec2 worldCoord = vec2(vFragPosition.x, -vFragPosition.y);
        vec4 flowTexel = texture(g_tFlowmap, vTexCoordOut / g_flFlowWorldUvScale);
        float flowNoise = texture(g_tFlowNoise, worldCoord * g_flFlowNoiseScale).y;

        float flowNormalAlpha;
        tangentNormal = GetFlowTangentNormal(worldCoord, flowTexel.xy, flowNoise, flowNormalAlpha);
        unnormalizedNormal = vec3(tangentNormal.xy, 0.0) + geometricNormal * tangentNormal.z;
        normal = normalize(unnormalizedNormal);

        if (F_COLOR_FLOW != 0)
        {
            vec4 flowColorTexel = GetFlowColor(worldCoord, flowTexel, flowNoise, eyeDirection, tangentNormal.xy, flowNormalAlpha);
            flowColor = flowColorTexel.rgb;
            flowColorAmount = saturate(flowColorTexel.a * 2.0);

            // Where the color covers the surface it shows no reflection
            flowReflectionMask = 1.0 - smoothstep(0.0, 1.0, saturate((flowColorTexel.a - 0.5) * 5.0));
        }
    }
    else if (F_BUMPMAP != 0)
    {
        vec4 normalTexel = texture(g_tNormal, vTexCoordOut);
        tangentNormal = normalTexel.xyz * 2.0 - 1.0;
        normalAlpha = normalTexel.a;
        mat3 tangentFrame = GetSource1TangentFrame(geometricNormal, vec4(0.0), vFragPosition, vTexCoordOut);
        unnormalizedNormal = tangentFrame * tangentNormal;
        normal = normalize(unnormalizedNormal);
    }

    if (F_REFRACT == 0)
    {
        // The cheap water takes the bump map as it is, without normalizing it
        float facing = dot(unnormalizedNormal, eyeDirection);
        vec3 reflectionDirection = 2.0 * facing * unnormalizedNormal - dot(unnormalizedNormal, unnormalizedNormal) * eyeDirection;
        vec3 cheapReflection;

        if (F_ENVMAP != 0)
        {
            cheapReflection = SampleSource1Cube(reflectionDirection);
        }
        else
        {
            cheapReflection = max(GetSkyAmbientLighting(reflectionDirection), vec3(0.0));
        }

        // Without a fresnel term the reflection takes the blend factor of the material, kept in the alpha of the tint
        float cheapFresnel = g_vReflectTint.w;

        if (F_NO_FRESNEL == 0)
        {
            cheapFresnel = pow(1.0 - saturate(facing), 5.0);
        }

        vec3 cheapColor = cheapReflection * g_vReflectTint.rgb;
        float cheapAlpha = 1.0;

        if (F_TRANSLUCENT == 0)
        {
            // The opaque variant mixes the reflection with the water color
            vec3 waterColor = mix(g_vWaterFogColor, flowColor, flowColorAmount);
            cheapColor = mix(waterColor, cheapColor, cheapFresnel * flowReflectionMask);
        }
        else
        {
            // The blended variant lays the reflection over the scene, more of it with distance, and the flowing color over both
            float distanceReflection = saturate((length(cameraRay) - g_flCheapWaterStartDistance) / max(g_flCheapWaterEndDistance - g_flCheapWaterStartDistance, 1.0));
            float reflectionAlpha = saturate(cheapFresnel + distanceReflection) * flowReflectionMask;
            cheapAlpha = flowColorAmount + reflectionAlpha * (1.0 - flowColorAmount);
            vec3 coveredColor = flowColor * flowColorAmount + cheapColor * reflectionAlpha * (1.0 - flowColorAmount);
            cheapColor = coveredColor / max(cheapAlpha, 0.0001);
        }

        ApplyFog(cheapColor, vFragPosition);
        outputColor = vec4(cheapColor, cheapAlpha);
        ApplyWaterRenderModes(geometricNormal, normal, tangentNormal, eyeDirection);
        return;
    }

    vec3 reflection;

    if (F_ENVMAP != 0)
    {
        reflection = SampleSource1Cube(reflect(-eyeDirection, normal));
    }
    else
    {
        reflection = max(GetSkyAmbientLighting(reflect(-eyeDirection, normal)), vec3(0.0));
    }

    reflection *= g_vReflectTint.rgb;

    float facing = saturate(dot(normal, eyeDirection));
    float fresnel = pow(1.0 - facing, 5.0);
    float distanceReflection = saturate((length(cameraRay) - g_flCheapWaterStartDistance) / max(g_flCheapWaterEndDistance - g_flCheapWaterStartDistance, 1.0));
    float reflectionAmount = saturate(fresnel + distanceReflection);

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

    refraction = mix(refraction, flowColor, flowColorAmount);
    reflectionAmount *= flowReflectionMask;

    vec3 color = mix(refraction, reflection, reflectionAmount);
    ApplyFog(color, vFragPosition);
    outputColor = vec4(color, 1.0);
    ApplyWaterRenderModes(geometricNormal, normal, tangentNormal, eyeDirection);
}
