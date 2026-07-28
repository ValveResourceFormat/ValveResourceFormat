#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "citadel_overlay_features.slang"

#define IBL 1

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;

#if (F_DECAL_SPACE_NORMALS == 1)
    in vec4 vProjectionDir;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tNormalRoughness;
uniform sampler2D g_tSelfIllumTintMask; // r=self illum, g=tint mask
uniform sampler2D g_tAmbientOcclusion;

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

uniform vec2 g_vAlbedoTexcoordUVOffset = vec2(0.0);
uniform vec2 g_vAlbedoScrollSpeed = vec2(0.0);
uniform vec2 g_vNormalAndRoughnessTexcoordUVOffset = vec2(0.0);
uniform vec2 g_vNormalAndRoughnessScrollSpeed = vec2(0.0);
uniform vec2 g_vSelfIllumTexcoordOffset = vec2(0.0);
uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);

uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
uniform float g_flSelfIllumScale = 0.0;
uniform float g_flSelfIllumAlbedoFactor = 1.0;

// The decal writes each gbuffer target with its own opacity, so a material can lay down
// colour without disturbing the normal, or the other way round.
uniform vec2 g_vColorTranslucencyInputRange = vec2(0.0, 1.0);
uniform vec2 g_vColorTranslucencyOutputRange = vec2(0.0, 1.0);
uniform vec2 g_vNormalTranslucencyInputRange = vec2(0.0, 1.0);
uniform vec2 g_vNormalTranslucencyOutputRange = vec2(0.0, 1.0);
uniform vec2 g_vRoughnessSelfIllumAoTranslucencyInputRange = vec2(0.0, 1.0);
uniform vec2 g_vRoughnessSelfIllumAoTranslucencyOutputRange = vec2(0.0, 1.0);

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/deadlock.slang"

// Must be last
#include "common/lighting.slang"

vec2 ScrollTexCoord(vec2 offset, vec2 speed)
{
    return vTexCoordOut + fract(offset + speed * g_flTime);
}

float RemapTranslucency(float translucency, vec2 inputRange, vec2 outputRange)
{
    float span = inputRange.y - inputRange.x;
    float normalized = saturate((translucency - inputRange.x) / ((span == 0.0) ? 1.0 : span));
    return normalized * (outputRange.y - outputRange.x) + outputRange.x;
}

MaterialProperties_t GetMaterial(vec3 vertexNormal)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormal, vFragPosition, vTangentOut, vBitangentOut);

    #if (F_DECAL_SPACE_NORMALS == 1)
        // The decal brings its own frame, so its normal map reads the same whatever it
        // was projected onto.
        mat.GeometricNormal = normalize(cross(vProjectionDir.xyz, vTangentOut));
    #endif

    vec2 albedoTexCoord = ScrollTexCoord(g_vAlbedoTexcoordUVOffset, g_vAlbedoScrollSpeed);
    vec2 normalTexCoord = ScrollTexCoord(g_vNormalAndRoughnessTexcoordUVOffset, g_vNormalAndRoughnessScrollSpeed);
    vec2 selfIllumTexCoord = ScrollTexCoord(g_vSelfIllumTexcoordOffset, g_vSelfIllumScrollSpeed);

    vec4 color = texture(g_tColor, albedoTexCoord);
    vec4 normalRoughness = texture(g_tNormalRoughness, normalTexCoord);
    vec2 selfIllumTintMask = texture(g_tSelfIllumTintMask, selfIllumTexCoord).xy;
    float occlusion = texture(g_tAmbientOcclusion, albedoTexCoord).y;

    vec3 albedo = mix(color.rgb, color.rgb * g_vColorTint * vVertexColorOut.rgb, selfIllumTintMask.y);

    float illumMask = selfIllumTintMask.x;
    vec3 illumColor = mix(g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor);

    mat.Albedo = mix(albedo, illumColor, illumMask * saturate(g_flSelfIllumScale));
    mat.IllumColor = mat.Albedo * (illumMask * g_flSelfIllumScale);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalRoughness.rg);
    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.RoughnessTex = normalRoughness.bb;
    mat.AmbientOcclusion = occlusion;

    // A decal writes no metalness of its own.
    mat.Metalness = 0.0;

    float translucency = vVertexColorOut.a * color.a;

    mat.Opacity = RemapTranslucency(translucency, g_vColorTranslucencyInputRange, g_vColorTranslucencyOutputRange);

    // The other two opacities cannot be written per gbuffer target in a forward pass, so
    // they fade the normal and the roughness/occlusion back toward the untouched surface.
    float normalOpacity = RemapTranslucency(translucency, g_vNormalTranslucencyInputRange, g_vNormalTranslucencyOutputRange);
    float surfaceOpacity = RemapTranslucency(translucency, g_vRoughnessSelfIllumAoTranslucencyInputRange, g_vRoughnessSelfIllumAoTranslucencyOutputRange);

    mat.Normal = normalize(mix(mat.GeometricNormal, mat.Normal, normalOpacity));
    mat.RoughnessTex = mix(vec2(1.0), mat.RoughnessTex, surfaceOpacity);
    mat.AmbientOcclusion = mix(1.0, mat.AmbientOcclusion, surfaceOpacity);
    mat.IllumColor *= surfaceOpacity;

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo;
    mat.SpecularColor = GetSpecularColorCitadel(mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    MaterialProperties_t mat = GetMaterial(vertexNormal);

    LightingTerms_t lighting = CalculateLighting(mat);
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + mat.IllumColor;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor = vec4(combinedLighting, mat.Opacity);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoordOut.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vVertexColorOut;
    }
}
