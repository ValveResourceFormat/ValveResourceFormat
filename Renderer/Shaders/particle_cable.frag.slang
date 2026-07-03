#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/lighting_common.slang"
#include "common/fog.slang"
#include "common/shadowmapping.slang"

#define D_BAKED_LIGHTING_FROM_PROBE 0
#define S_SCENE_PROBE_TYPE 0

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    #include "common/lighting.lpv.slang"
#endif

// Render modes -- switched on/off by code
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_Illumination 0

uniform sampler2D g_tColor; // SrgbRead(true)

uniform float g_flRoughness = 1.0;
uniform float g_flDiffuseAmount = 1.0;
uniform float g_flSelfIllumAmount = 0.0;

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vColorOut;

out vec4 outputColor;

// cables.vfx lighting: wrapped diffuse and a normalized Blinn-Phong specular per direct light,
// both driven by g_flRoughness, over an ambient term. Output stays linear HDR; the post-process
// pass does the exposure and filmic tonemap.
void main(void)
{
    vec3 normal = normalize(vNormalOut);
    vec3 albedo = texture(g_tColor, vTexCoordOut).rgb * vColorOut.rgb;

    vec3 sunDirection = GetEnvLightDirection(0u);
    vec3 sunColor = GetLightColor(0u);

    float smoothness = 1.0 - clamp(g_flRoughness, 0.0, 1.0);
    float wrapPower = smoothness * 0.8;
    float sunWrap = pow(saturate(dot(normal, sunDirection)), wrapPower + 0.6) * ((wrapPower + 1.6) * 0.5);

    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 halfVector = normalize(sunDirection + viewDirection);
    float specularExponent = exp2(smoothness * 10.0);
    float specularNormalization = ((specularExponent + 2.0) * (specularExponent + 4.0)) / (8.0 * (exp2(specularExponent * -0.5) + specularExponent));
    vec3 specular = sunColor * (pow(saturate(dot(normal, halfVector)), specularExponent) * sunWrap * specularNormalization * (1.0 - saturate(g_flRoughness * 0.5)));

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    // Per-fragment probe irradiance provides the ambient term.
    MaterialProperties_t mat;
    InitProperties(mat, normal, vFragPosition, vec3(1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0));
    mat.AmbientNormal = normal;
    vec3 ambient = ComputeLightProbeShading(mat);

    #if (S_LIGHTMAP_VERSION_MINOR == 1)
        // The probe bakes all four direct lights (sun + local) into indices/strengths; shade each
        // with the cable's wrap/Blinn-Phong terms, like lighting.slang shades probe-lit geometry.
        vec3 probeShadowCoords = CalculateProbeShadowCoords(vFragPosition);
        vec4 vLightStrengths = pow2(textureLod(g_tLPV_Scalars, probeShadowCoords, 0.0));
        uvec4 vLightIndices = uvec4(textureLod(g_tLPV_Indices, probeShadowCoords, 0.0) * 255.0);

        vec3 directDiffuse = vec3(0.0);
        vec3 directSpecular = vec3(0.0);
        for (int i = 0; i < 4; i++)
        {
            if (vLightStrengths[i] <= 0.0001)
            {
                continue;
            }

            vec3 lightDirection = GetLightDirection(vFragPosition, vLightIndices[i]);
            vec3 lightColor = GetLightColor(vLightIndices[i]) * vLightStrengths[i];
            if (IsDirectionalLight(vLightIndices[i]))
            {
                // Dynamic-caster shadowing of the sun; static occlusion is already in the baked strength.
                lightColor *= CalculateSunShadowMapVisibility(vFragPosition);
            }
            float lightWrap = pow(saturate(dot(normal, lightDirection)), wrapPower + 0.6) * ((wrapPower + 1.6) * 0.5);
            vec3 lightHalfVector = normalize(lightDirection + viewDirection);

            directDiffuse += lightColor * lightWrap;
            directSpecular += lightColor * (pow(saturate(dot(normal, lightHalfVector)), specularExponent) * lightWrap * specularNormalization * (1.0 - saturate(g_flRoughness * 0.5)));
        }
    #elif (S_LIGHTMAP_VERSION_MINOR >= 2)
        // Baked sun visibility from the probe volume; local lights are already in the irradiance.
        vec4 dlsh = textureLod(g_tLPV_Shadows, CalculateProbeShadowCoords(vFragPosition), 0.0);
        float sunVisibility = g_nNumLights[0] > 0u ? saturate(1.0 - dot(dlsh, g_vSunLightBakedShadowMask)) : 1.0;
        sunVisibility *= CalculateSunShadowMapVisibility(vFragPosition);

        vec3 directDiffuse = sunColor * sunWrap * sunVisibility;
        vec3 directSpecular = specular * sunVisibility;
    #else
        vec3 directDiffuse = sunColor * sunWrap;
        vec3 directSpecular = specular;
    #endif
#else
    vec3 ambient = vec3(0.3) * (dot(vec3(0.6, 0.4, 1.0), pow2(saturate(normal))) + dot(vec3(0.6, 0.4, 0.2), pow2(saturate(-normal))));
    vec3 directDiffuse = sunColor * sunWrap;
    vec3 directSpecular = specular;
#endif

    vec3 color = directSpecular + albedo * ((ambient + directDiffuse) * g_flDiffuseAmount + g_flSelfIllumAmount);

    if (g_iRenderMode == renderMode_FullBright)
    {
        color = albedo;
    }

    ApplyFog(color, vFragPosition);

    if (g_iRenderMode == renderMode_Color)
    {
        color = albedo;
    }
    else if (g_iRenderMode == renderMode_Normals)
    {
        color = PackToColor(normal);
    }
    else if (g_iRenderMode == renderMode_Illumination)
    {
        color = SrgbLinearToGamma(directDiffuse + directSpecular);
    }

    outputColor = vec4(color, 1.0);
}
