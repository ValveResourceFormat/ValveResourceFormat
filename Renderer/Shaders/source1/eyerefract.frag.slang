#version 460

#include "eyes_features.slang"
#include "eyes_common_ps.slang"
#include "common_envmap_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tIris; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tCornea;
uniform sampler2D g_tAmbientOccl;

uniform int F_RAYTRACE_SPHERE;
uniform int F_SPHERE_TEXKILL;
uniform float g_flDilation = 0.5;
uniform float g_flGlossiness = 1.0;
uniform float g_flCorneaBumpStrength = 1.0;
uniform float g_flParallaxStrength = 0.25;
uniform float g_flEyeballRadius = 0.5;
uniform vec3 g_vAmbientOcclColor = vec3(0.33);

// The distance along the ray to where it enters the sphere, or 0 when it misses
float IntersectRaySphere(vec3 rayOrigin, vec3 ray, vec3 sphereCenter, float sphereRadius)
{
    vec3 toOrigin = rayOrigin - sphereCenter;
    float b = dot(toOrigin, ray);
    float c = dot(toOrigin, toOrigin) - sphereRadius * sphereRadius;
    float discriminant = b * b - c;

    return discriminant > 0.0 ? -b - sqrt(discriminant) : 0.0;
}

vec3 WorldToTangent(vec3 v, vec3 normal, vec3 tangent, vec3 binormal)
{
    return vec3(dot(v, tangent), dot(v, binormal), dot(v, normal));
}

vec3 TangentToWorld(vec3 v, vec3 normal, vec3 tangent, vec3 binormal)
{
    return v.x * tangent + v.y * binormal + v.z * normal;
}

// The average luminance of the ambient light
float GetAverageAmbient()
{
    vec3 sum = GetSource1Ambient(vec3(1.0, 0.0, 0.0)) + GetSource1Ambient(vec3(-1.0, 0.0, 0.0))
        + GetSource1Ambient(vec3(0.0, 1.0, 0.0)) + GetSource1Ambient(vec3(0.0, -1.0, 0.0))
        + GetSource1Ambient(vec3(0.0, 0.0, 1.0)) + GetSource1Ambient(vec3(0.0, 0.0, -1.0));

    return saturate(dot(sum / 6.0, vec3(0.299, 0.587, 0.114)));
}

void main()
{
    vec3 normal = normalize(vFragPosition - g_vEyeOrigin);

    // The iris projection vectors point away from the up and left of the eye socket
    vec3 socketUp = normalize(-g_vIrisProjectionV.xyz);
    vec3 socketLeft = normalize(-g_vIrisProjectionU.xyz);

    vec3 tangent = normalize(cross(socketUp, normal));
    vec3 binormal = normalize(cross(normal, tangent));

    vec3 viewDirection = normalize(vFragPosition - g_vCameraPositionWs);
    vec3 tangentView = normalize(WorldToTangent(viewDirection, normal, tangent, binormal));

    // The eyeball geometry is not always round, so the cornea is found on a sphere
    vec3 position = vFragPosition;

    if (F_RAYTRACE_SPHERE != 0)
    {
        float distance = IntersectRaySphere(g_vCameraPositionWs, viewDirection, g_vEyeOrigin, g_flEyeballRadius);
        position = g_vCameraPositionWs + viewDirection * distance;

        if (distance == 0.0)
        {
            if (F_SPHERE_TEXKILL != 0)
            {
                discard;
            }

            position = g_vEyeOrigin + normal * g_flEyeballRadius;
        }
    }

    vec2 corneaCoords = GetSource1IrisCoords(position);
    vec2 sphereCoords = corneaCoords * 0.5 + 0.25;

    vec4 corneaSample = texture(g_tCornea, corneaCoords);

    // The iris is pushed back from the cornea by the depth in its blue channel
    vec2 parallax = (tangentView.xy * corneaSample.b * g_flParallaxStrength) / (1.0 - tangentView.z);
    parallax.x = -parallax.x;

    vec2 irisCoords = sphereCoords - parallax;
    float corneaNoise = texture(g_tIris, sphereCoords + parallax * 0.5).a;

    vec3 corneaTangentNormal = vec3((corneaSample.rg - 0.5) * g_flCorneaBumpStrength + corneaNoise * 0.1, 1.0);
    corneaTangentNormal = normalize(corneaTangentNormal);
    vec3 corneaNormal = normalize(TangentToWorld(corneaTangentNormal, normal, tangent, binormal));

    // The pupil grows toward the dilation, around the center of the iris texture
    irisCoords -= 0.5;
    float pupilCenterToBorder = saturate(length(irisCoords) / 0.2);
    irisCoords *= mix(1.0, pupilCenterToBorder, saturate(g_flDilation) * 2.5 - 1.25);
    irisCoords += 0.5;

    vec3 irisColor = texture(g_tIris, irisCoords).rgb;

    // The sides of the eyeball are flattened so the lighting does not darken them
    float normalDotLeft = -dot(normal, socketLeft) * 0.5;
    vec3 bentNormal = normalize(normalDotLeft * socketLeft + normal);

    vec3 lightDirections[SOURCE1_EYE_MAX_LIGHTS];
    vec3 lightColors[SOURCE1_EYE_MAX_LIGHTS];
    int lightCount = GatherSource1EyeLights(vFragPosition, lightDirections, lightColors);

    vec3 vertexLight = GetSource1Ambient(bentNormal);
    vec3 irisLighting = vec3(0.0);
    vec3 specularHighlights = vec3(0.0);

    float irisHighlightMask = corneaSample.a;
    vec3 irisTangentNormal = corneaTangentNormal;
    irisTangentNormal.xy *= -2.5;

    vec3 corneaReflection = reflect(viewDirection, corneaNormal);

    for (int i = 0; i < lightCount; i++)
    {
        vec3 lightDirection = lightDirections[i];
        vec3 lightColor = lightColors[i];

        // The cosine term of a light uses the bent normal, or the plain one when a light warp texture shapes it
        vec3 diffuseNormal = F_LIGHTWARP != 0 ? normal : bentNormal;
        vertexLight += lightColor * GetSource1DiffuseTerm(dot(diffuseNormal, lightDirection));

        vec3 tangentLight = WorldToTangent(lightDirection, normal, tangent, binormal);

        vec3 flattened = -tangentLight;
        flattened.xy *= -0.5;
        flattened.z = max(flattened.z, 0.5);
        flattened = normalize(flattened);

        float irisFacing = pow(abs(dot(irisTangentNormal, flattened)), 6.0) * 0.5;

        // Fades the highlights of a light that falls behind the eyeball
        float coneOfDarkness = pow(1.0 - saturate((-tangentLight.z - 0.25) / 0.75), 4.0);

        irisLighting += irisFacing * irisHighlightMask * coneOfDarkness * lightColor;
        specularHighlights += pow(saturate(dot(corneaReflection, lightDirection)), 128.0) * lightColor;
    }

    vertexLight = GetSource1FullbrightFallback(vertexLight, bentNormal);

    irisLighting += saturate(dot(irisTangentNormal, -tangentView)) * GetAverageAmbient() * irisHighlightMask * 0.5;

    vec3 ambientOcclusion = texture(g_tAmbientOccl, vTexCoordOut).rgb;
    vertexLight *= mix(g_vAmbientOcclColor, vec3(1.0), ambientOcclusion);

    vec3 reflection = g_flGlossiness * texture(g_tEnvMap, corneaReflection).rgb;

    vec3 color = irisColor + corneaNoise * 0.1;
    color *= vertexLight + irisLighting;
    color += reflection * vertexLight;
    color += specularHighlights;

    ApplyFog(color, vFragPosition);
    outputColor = vec4(color, 1.0);
}
