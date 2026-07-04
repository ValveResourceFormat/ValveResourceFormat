#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/texturing.slang"
#include "common/fog.slang"

// cables.vfx

uniform sampler2D g_tColor; // SrgbRead(true)

uniform float g_flRoughness = 1.0;
uniform float g_flDiffuseAmount = 1.0;
uniform float g_flSelfIllumAmount = 0.0;

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vColorOut;

out vec4 outputColor;

#include "common/lighting.slang"

void main(void)
{
    vec3 normal = normalize(vNormalOut);
    vec3 albedo = texture(g_tColor, vTexCoordOut).rgb * vColorOut.rgb;

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

    if (g_iRenderMode == renderMode_FullBright)
    {
        color = albedo;
    }

    ApplyFog(color, vFragPosition);

    outputColor = vec4(color, 1.0);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoordOut.xy))
    {
        //
    }
}
