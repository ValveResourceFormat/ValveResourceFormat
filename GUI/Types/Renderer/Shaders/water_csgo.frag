#version 460

#include "common/ViewConstants.glsl"
#include "common/utils.glsl"
#include "common/fog.glsl"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec3 vBitangentOut;
in vec4 vColorBlendValues;

out vec4 outputColor;

//uniform sampler2D g_tColor; // SrgbRead(true)
//uniform sampler2D g_tDebris;
//uniform sampler2D g_tDebrisNormal;
//uniform sampler2D g_tSceneDepth;

uniform vec4 g_vWaterFogColor;
uniform vec4 g_vWaterDecayColor;

//Main entry point
void main()
{
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

    // Calculate fresnel (assuming Vertex Normal = Z up?)
    float fresnel = ClampToPositive(1.0 - pow(viewDirection.z, 0.5));

    float fog_factor = max(pow(fresnel, 0.8), 0.5);
    float decay_factor = min(pow(fresnel, 0.6), 0.85);

    vec3 color = mix(g_vWaterFogColor.rgb, g_vWaterDecayColor.rgb, decay_factor);
    outputColor = vec4(SrgbGammaToLinear(color), max(decay_factor, fog_factor));
    ApplyFog(outputColor.rgb, vFragPosition);
}
