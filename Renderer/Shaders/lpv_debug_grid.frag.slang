#version 460

#include "common/utils.slang"
#include "common/features.slang"

#define IBL 1
#define D_POINT_TO_CUBE 0

#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0

in vec3 vFragPosition;
in vec3 vCubeNormal;
flat in vec3 vSampleCenter;

out vec4 outputColor;

uniform float g_flPointSize = 4.0;
uniform vec3 g_vAlbedo = vec3(0.5); // SrgbRead(true)
uniform float g_flRoughness = 0.5;
uniform float g_flMetalness = 0.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/texturing.slang"
#include "common/fog.slang"

// Must be last
#include "common/lighting.slang"

void main()
{
#if (D_POINT_TO_CUBE == 1)
    vec3 position = vFragPosition;
    vec3 normal = normalize(vCubeNormal);
#else
    // The quad only bounds the sphere, trace it for the surface and its depth
    vec3 rayDirection = normalize(vFragPosition - g_vCameraPositionWs);
    vec3 fromCenter = g_vCameraPositionWs - vSampleCenter;
    float b = dot(fromCenter, rayDirection);
    float c = dot(fromCenter, fromCenter) - g_flPointSize * g_flPointSize;
    float discriminant = b * b - c;

    if (discriminant < 0.0)
    {
        discard;
    }

    vec3 position = g_vCameraPositionWs + rayDirection * (-b - sqrt(discriminant));
    vec3 normal = (position - vSampleCenter) / g_flPointSize;

    // Through the depth range, which the 3D skybox and viewmodel are drawn into a slice of
    vec4 clipPosition = g_matWorldToProjection * vec4(position, 1.0);
    gl_FragDepth = gl_DepthRange.diff * (clipPosition.z / clipPosition.w) + gl_DepthRange.near;
#endif

    vec3 tangent = normalize(cross(abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0), normal));
    vec3 bitangent = cross(normal, tangent);

    MaterialProperties_t mat;
    InitProperties(mat, normal, position, tangent, bitangent);

    mat.Albedo = g_vAlbedo;
    mat.Metalness = g_flMetalness;
    mat.Normal = mat.GeometricNormal;
    mat.RoughnessTex = vec2(g_flRoughness);
    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = mix(vec3(0.04), mat.Albedo, mat.Metalness);

    LightingTerms_t lighting = CalculateLighting(mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    outputColor = vec4(mat.DiffuseColor * diffuseLighting + specularLighting, 1.0);

    ApplyFog(outputColor.rgb, mat.PositionWS);

    HandleLightingRenderModes(outputColor, mat, lighting);
}
