#version 460

#ifndef VRF_SHADOW_PCF_INCLUDE
#define VRF_SHADOW_PCF_INCLUDE

// We currently have a separate shadow map for all barn lights
uniform sampler2DShadow g_tBarnLightShadowDepth; // reserved

// Added to the depth of a point in a light's shadow map before it is compared
const float SHADOW_RECEIVER_DEPTH_BIAS = -0.000015;

// Fixed 3x3 gaussian weights
float ComputeShadow_PCF_3x3_Gaussian(sampler2DShadow shadowMap, vec2 shadowUV, float shadowRef, vec2 texelSize)
{
    float shadow = dot(vec4(
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2( 1.0,  1.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2(-1.0,  1.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2( 1.0, -1.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2(-1.0, -1.0), shadowRef), 0.0)
    ), vec4(0.25));

    if (shadow == 0.0 || shadow == 1.0)
        return shadow;

    shadow *= 0.2996;
    shadow += dot(vec4(
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2( 1.0,  0.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2(-1.0,  0.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2( 0.0, -1.0), shadowRef), 0.0),
        textureLod(shadowMap, vec3(shadowUV + texelSize * vec2( 0.0,  1.0), shadowRef), 0.0)
    ), vec4(0.1236));
    shadow += textureLod(shadowMap, vec3(shadowUV, shadowRef), 0.0) * 0.206;

    return shadow;
}

float ComputeBarnLightShadow_PCF_3x3_Gaussian(vec2 shadowUV, float shadowRef, vec2 texelSize)
{
    return ComputeShadow_PCF_3x3_Gaussian(g_tBarnLightShadowDepth, shadowUV, shadowRef, texelSize);
}

#endif
