#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/texturing.slang"
#include "common/fog.slang"

// cables.vfx

#define S_MODE_DEPTH 0
#define F_SELF_ILLUM 0
#define F_ALPHA_TEST 0

uniform sampler2D g_tColor; // SrgbRead(true)

uniform float g_flRoughness = 1.0;
uniform float g_flDiffuseAmount = 1.0;
uniform float g_flSelfIllumAmount = 0.0;

#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference = 0.5;
#endif

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumMask;
    uniform float g_flOverbrightness = 0.0;
    uniform float g_flSelfIllumAlbedoFactor = 1.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
#endif

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vColorOut;

#if (S_MODE_DEPTH == 1)

#if (F_ALPHA_TEST == 1)
void main(void)
{
    if (texture(g_tColor, vTexCoordOut).a * vColorOut.a - g_flAlphaTestReference < 0.001)
    {
        discard;
    }
}
#else
layout (early_fragment_tests) in;

void main(void)
{
}
#endif

#else

out vec4 outputColor;

#define ENABLE_LPV_BLENDING

#include "common/lighting.slang"

void main(void)
{
    vec3 normal = normalize(vNormalOut);
    vec4 colorTexture = texture(g_tColor, vTexCoordOut);
    vec3 albedo = colorTexture.rgb * vColorOut.rgb;

    float opacity = colorTexture.a * vColorOut.a;

#if (F_ALPHA_TEST == 1)
    if (opacity - g_flAlphaTestReference < 0.001)
    {
        discard;
    }
#endif

    // The tube has no tangent-space textures, so any orthonormal tangent frame will do.
    vec3 tangent = normalize(cross(normal, abs(normal.z) < 0.9 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0)));

    MaterialProperties_t mat;
    InitProperties(mat, normal, vFragPosition, tangent, cross(normal, tangent));
    mat.Albedo = albedo;
    mat.DiffuseColor = albedo;
    mat.Normal = normal;
    mat.AmbientNormal = normal;
    mat.AmbientGeometricNormal = normal;
    mat.Roughness = vec2(saturate(g_flRoughness));

    LightingTerms_t lighting = CalculateLighting(mat);
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;
    vec3 color = (mat.DiffuseColor * (diffuseLighting * g_flDiffuseAmount + g_flSelfIllumAmount)) + specularLighting;

#if (F_SELF_ILLUM == 1)
    vec3 selfIllumMask = texture(g_tSelfIllumMask, vTexCoordOut + fract(g_vSelfIllumScrollSpeed * g_flTime)).rgb;
    vec3 selfIllum = selfIllumMask * (exp2(g_flSelfIllumBrightness) * g_flSelfIllumScale) * g_vSelfIllumTint;
    color += mix(selfIllum, selfIllum * albedo, g_flSelfIllumAlbedoFactor);
#endif

    if (g_iRenderMode == renderMode_FullBright)
    {
        color = albedo;
    }

    ApplyFog(color, vFragPosition);

#if (F_SELF_ILLUM == 1)
    color *= 1.0 + g_flOverbrightness * vColorOut.a;
#endif

    outputColor = vec4(color, opacity);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoordOut.xy))
    {
        //
    }
}

#endif
