#version 460

#define renderMode_Cubemaps 0

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vColorBlendValues;

out vec4 outputColor;

//uniform sampler2D g_tColor; // SrgbRead(true)
//uniform sampler2D g_tDebris;
//uniform sampler2D g_tDebrisNormal;
//uniform sampler2D g_tSceneDepth;

uniform vec4 g_vWaterFogColor;
uniform vec4 g_vWaterDecayColor;

#include "common/features.glsl"
#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
#include "common/lighting_common.glsl"
#include "common/utils.glsl"
#include "common/fullbright.glsl"
#include "common/texturing.glsl"
#include "common/pbr.glsl"
#include "common/fog.glsl"
#include "common/environment.glsl"
#include "common/lighting.glsl"

//Main entry point
void main()
{
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

    float fresnel = clamp(1.0 - pow(viewDirection.z, 0.5), 0.2, 1.0);

    float fog_factor = max(pow(fresnel, 0.8), 0.5);
    float decay_factor = min(pow(fresnel, 0.6), 0.85);

    vec3 normal = vNormalOut;

    MaterialProperties_t material;
    InitProperties(material, normal);
    material.Roughness = 0.0001;
    material.AmbientNormal = normal;
    material.SpecularColor = vec3(1.0);
    vec3 reflectionColor = GetEnvironment(material);

    vec3 color = mix(g_vWaterFogColor.rgb, g_vWaterDecayColor.rgb, decay_factor);
    outputColor = vec4(SrgbGammaToLinear(color), max(decay_factor, fog_factor));
    ApplyFog(outputColor.rgb, vFragPosition);

    outputColor.rgb =  mix(outputColor.rgb, reflectionColor,  fresnel);
}
