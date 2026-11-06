#version 460

#include "eyes_features.slang"
#include "eyes_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tIris; // SrgbRead(true)

// The glints are small highlights of the lights on the cornea. They fade out in a dark scene.
#define SOURCE1_EYE_GLINT_EXPONENT 512.0

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);
    vec4 irisColor = texture(g_tIris, GetSource1IrisCoords(vFragPosition));

    // Flattens the sides of the eyeball so the vertex lighting does not darken them
    vec3 fromEye = vFragPosition - vEyeOrigin;
    vec3 eyeUp = normalize(-vIrisProjectionV.xyz);
    float normalDotUp = -dot(fromEye, eyeUp) * 0.5;
    vec3 normal = normalize(normalDotUp * eyeUp + fromEye);

    vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 diffuseLight = GetSource1Ambient(normal);
    vec3 specularLight = vec3(0.0);
    vec3 rimLight = vec3(0.0);
    AddSource1SceneLights(diffuseLight, specularLight, rimLight, vFragPosition, normal, eyeDirection, 1.0, 0.0);
    diffuseLight = GetSource1FullbrightFallback(diffuseLight, normal);

    vec3 color = mix(baseColor.rgb, irisColor.rgb, irisColor.a) * diffuseLight;

    // The glint of a light sits where the surface of the eye faces halfway between the light and the viewer
    vec3 sphereNormal = normalize(fromEye);
    vec3 lightDirections[SOURCE1_EYE_MAX_LIGHTS];
    vec3 lightColors[SOURCE1_EYE_MAX_LIGHTS];
    int lightCount = GatherSource1EyeLights(vFragPosition, normal, lightDirections, lightColors);
    vec3 glint = vec3(0.0);

    for (int i = 0; i < lightCount; i++)
    {
        vec3 halfway = normalize(lightDirections[i] + eyeDirection);
        glint += pow(saturate(dot(sphereNormal, halfway)), SOURCE1_EYE_GLINT_EXPONENT) * lightColors[i];
    }

    float ambientLuminance = saturate(dot(GetSource1Ambient(eyeDirection), SOURCE1_LUMA_WEIGHTS));
    color += glint * smoothstep(0.0, 0.01, ambientLuminance);

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, 1.0);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));

    if (g_iRenderMode != 0)
    {
        vec3 albedo = mix(baseColor.rgb, irisColor.rgb, irisColor.a);
        vec3 tangent = normalize(cross(normal, vec3(0.0, 0.0, 1.0)));

        ApplySource1RenderModes(outputColor, albedo, vec4(1.0), normal, eyeDirection, g_tColor, vTexCoordOut);
        ApplySource1NormalRenderModes(outputColor, normal, tangent, vec3(0.0, 0.0, 1.0), normal);
        ApplySource1LightingRenderModes(outputColor, diffuseLight, specularLight);
    }
}
