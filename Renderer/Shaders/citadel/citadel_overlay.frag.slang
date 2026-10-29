#version 460

#include "citadel_overlay_features.slang"

// Rect lights shade as area lights, the way Deadlock does it
#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/citadel.slang"

#if (F_DECAL_SPACE_NORMALS == 1)
    in vec4 vProjectionDir;
#endif

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tNormalRoughness;
uniform sampler2D g_tSelfIllumTintMask; // r=self illum, g=tint mask

uniform vec3 g_vColorTint = vec3(1.0);

uniform vec2 g_vAlbedoTexcoordUVOffset = vec2(0.0);
uniform vec2 g_vAlbedoScrollSpeed = vec2(0.0);
uniform vec2 g_vNormalAndRoughnessTexcoordUVOffset = vec2(0.0);
uniform vec2 g_vNormalAndRoughnessScrollSpeed = vec2(0.0);
uniform vec2 g_vSelfIllumTexcoordOffset = vec2(0.0);
uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);

uniform vec3 g_vSelfIllumTint = vec3(1.0);
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

vec2 ScrollTexCoordOffset(vec2 texCoord, vec2 offset, vec2 speed)
{
    return texCoord + fract(offset + speed * g_flTime);
}

float RemapTranslucency(float translucency, vec2 inputRange, vec2 outputRange)
{
    float span = inputRange.y - inputRange.x;
    float normalized = saturate((translucency - inputRange.x) / ((span == 0.0) ? 1.0 : span));
    return normalized * (outputRange.y - outputRange.x) + outputRange.x;
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

#if (F_DECAL_SPACE_NORMALS == 1)
    // The decal brings its own frame, so its normal map reads the same whatever it
    // was projected onto.
    mat.GeometricNormal = normalize(cross(vProjectionDir.xyz, vTangentOut.xyz));
#endif

    vec2 albedoTexCoord = ScrollTexCoordOffset(texCoord, g_vAlbedoTexcoordUVOffset, g_vAlbedoScrollSpeed);
    vec2 normalTexCoord = ScrollTexCoordOffset(texCoord, g_vNormalAndRoughnessTexcoordUVOffset, g_vNormalAndRoughnessScrollSpeed);
    vec2 selfIllumTexCoord = ScrollTexCoordOffset(texCoord, g_vSelfIllumTexcoordOffset, g_vSelfIllumScrollSpeed);

    vec4 color = texture(g_tColor, albedoTexCoord);
    vec4 normalRoughness = texture(g_tNormalRoughness, normalTexCoord);
    vec2 selfIllumTintMask = texture(g_tSelfIllumTintMask, selfIllumTexCoord).xy;

    vec3 albedo = mix(color.rgb, color.rgb * g_vColorTint * vVertexColorOut.rgb, selfIllumTintMask.y);

    float illumMask = selfIllumTintMask.x;
    vec3 illumColor = mix(g_vSelfIllumTint, g_vSelfIllumTint * albedo, g_flSelfIllumAlbedoFactor);

    mat.Albedo = mix(albedo, illumColor, illumMask * saturate(g_flSelfIllumScale));
    mat.IllumColor = mat.Albedo * (illumMask * g_flSelfIllumScale);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalRoughness.rg);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.RoughnessTex = normalRoughness.bb;

    // A decal writes no metalness of its own.
    mat.Metalness = 0.0;

    float translucency = vVertexColorOut.a * color.a;

    mat.Opacity = RemapTranslucency(translucency, g_vColorTranslucencyInputRange, g_vColorTranslucencyOutputRange);

    // The other two opacities cannot be written per gbuffer target in a forward pass, so
    // they fade the normal and the roughness back toward the untouched surface.
    float normalOpacity = RemapTranslucency(translucency, g_vNormalTranslucencyInputRange, g_vNormalTranslucencyOutputRange);
    float surfaceOpacity = RemapTranslucency(translucency, g_vRoughnessSelfIllumAoTranslucencyInputRange, g_vRoughnessSelfIllumAoTranslucencyOutputRange);

    mat.Normal = normalize(mix(mat.GeometricNormal, mat.Normal, normalOpacity));
    mat.RoughnessTex = mix(vec2(1.0), mat.RoughnessTex, surfaceOpacity);
    mat.IllumColor *= surfaceOpacity;

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    ResolveDiffuseSpecularCitadel(mat);

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
