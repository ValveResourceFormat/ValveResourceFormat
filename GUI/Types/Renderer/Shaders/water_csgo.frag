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

uniform sampler2D g_tWavesNormalHeight;
uniform vec4 g_vWaveScale = vec4(1.0);
uniform float g_flWavesSpeed = 1.0;

uniform float g_flSkyBoxScale = 1.0;
uniform float g_flSkyBoxFadeRange;
uniform vec4 g_vMapUVMin = vec4(-1000.0);
uniform vec4 g_vMapUVMax = vec4(1000.0);

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
    vec4 noise = vec4(1.0); // todo: add blue noise

    float renderScale = g_bIsSkybox ? g_flSkyBoxScale : 1.0;

    vec3 cameraPosition = vFragPosition - g_vCameraPositionWs;

    vec3 viewDirection = normalize(cameraPosition);
    vec3 viewDirectionInv = -viewDirection;
    float viewDistance = length(cameraPosition) * renderScale;

    if (!g_bIsSkybox)
    {
        vec2 vMapUv = (vFragPosition.xy - g_vMapUVMin.xy) / (g_vMapUVMax.xy - g_vMapUVMin.xy);
        vMapUv.y = 1.0 - vMapUv.y;

        vec2 vMapCenteredUv = abs(vec2(0.5) - vMapUv) * 2.0;
        if ((saturate(1.0 - saturate((max(vMapCenteredUv.x, vMapCenteredUv.y) - (1.0 - g_flSkyBoxFadeRange)) / g_flSkyBoxFadeRange)) - noise.x) < 0.0)
        {
           discard;
        }
    }

    float fresnel = clamp(1.0 - pow(viewDirectionInv.z, 0.5), 0.2, 1.0);

    float fog_factor = max(pow(fresnel, 0.8), 0.5);
    float decay_factor = min(pow(fresnel, 0.6), 0.85);

    vec3 normal = vNormalOut;

    vec3 wavesNormalHeight = texture(g_tWavesNormalHeight, vTexCoordOut + (g_flWavesSpeed * g_flTime) / g_vWaveScale.xy).xyz;

    MaterialProperties_t material;
    InitProperties(material, normal);
    material.Roughness = vec2(0.01);
    material.NormalMap = DecodeHemiOctahedronNormal(wavesNormalHeight.xy);
    material.Normal = calculateWorldNormal(material.NormalMap, material.GeometricNormal, material.Tangent, material.Bitangent);
    material.Height = wavesNormalHeight.z;
    material.AmbientNormal = material.Normal;
    material.SpecularColor = vec3(1.0);
    vec3 reflectionColor = GetEnvironment(material);

    vec3 color = SrgbGammaToLinear(mix(g_vWaterFogColor.rgb, g_vWaterDecayColor.rgb, decay_factor));

    LightingTerms_t lighting = CalculateLighting(material);

    const float shadowFactor = 0.6;
    color *= mix(lighting.DiffuseDirect, vec3(1.0), shadowFactor);

    color = mix(color, reflectionColor, fresnel);

    color *= lighting.DiffuseIndirect;

    float alpha = max(decay_factor, fog_factor);

    outputColor = vec4(color, alpha);

    ApplyFog(outputColor.rgb, vFragPosition);
    HandleMaterialRenderModes(outputColor, material);
}
