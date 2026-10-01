#version 460

#include "common/utils.slang"
#include "common/features.slang"

#define IBL 1

#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0

in vec3 vFragPosition;
flat in vec3 vSampleCenter;

out vec4 outputColor;

uniform float g_flSampleSize = 10.0;
uniform vec3 g_vAlbedo = vec3(1.0); // SrgbRead(true)
uniform float g_flRoughness = 1.0;
uniform float g_flMetalness = 0.0;
uniform float g_flNearPlane = 1.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/texturing.slang"
#include "common/fog.slang"

// Must be last
#include "common/lighting.slang"

// Distance along a ray to where it enters a sphere, negative when it misses
float SphereIntersect(vec3 rayOrigin, vec3 rayDirection, vec4 sphere)
{
    vec3 toOrigin = rayOrigin - sphere.xyz;
    float b = dot(toOrigin, rayDirection);
    float c = dot(toOrigin, toOrigin) - sphere.w * sphere.w;
    float h = b * b - c;

    if (h < 0.0)
    {
        return -1.0;
    }

    return -b - sqrt(h);
}

// How opaque a pixel has to be to draw, so a fading surface thins out in an ordered pattern
float OpaqueFadeThreshold()
{
    const float bayer[16] = float[16](
        0.0, 8.0, 2.0, 10.0,
        12.0, 4.0, 14.0, 6.0,
        3.0, 11.0, 1.0, 9.0,
        15.0, 7.0, 13.0, 5.0);

    ivec2 pixel = ivec2(gl_FragCoord.xy) & 3;
    return (bayer[pixel.y * 4 + pixel.x] + 0.5) / 16.0;
}

void main()
{
    // The quad only bounds the sphere, trace it for the surface
    vec3 rayDirection = normalize(vFragPosition - g_vCameraPositionWs);
    float hitDistance = SphereIntersect(g_vCameraPositionWs, rayDirection, vec4(vSampleCenter, g_flSampleSize));

    // Fades out towards the near plane, and a ray that misses has no surface at all
    float opacity = smoothstep(g_flNearPlane, g_flNearPlane + 1, hitDistance);

    if (opacity < OpaqueFadeThreshold())
    {
        discard;
    }

    vec3 position = g_vCameraPositionWs + rayDirection * hitDistance;
    vec3 normal = normalize(position - vSampleCenter);

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
