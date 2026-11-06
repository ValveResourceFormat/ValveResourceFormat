#version 460

#include "eyes_features.slang"
#include "eyes_common_ps.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
flat in vec3 vMouthForward;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    vec3 normal = normalize(vNormalOut);
    vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);

    // The teeth take the plain cosine of each light, and are darker the more they face away from the mouth
    vec3 diffuseLight = GetSource1Ambient(normal);

    vec3 lightDirections[SOURCE1_EYE_MAX_LIGHTS];
    vec3 lightColors[SOURCE1_EYE_MAX_LIGHTS];
    int lightCount = GatherSource1EyeLights(vFragPosition, normal, lightDirections, lightColors);

    for (int i = 0; i < lightCount; i++)
    {
        diffuseLight += lightColors[i] * saturate(dot(normal, lightDirections[i]));
    }

    diffuseLight = GetSource1FullbrightFallback(diffuseLight, normal);
    diffuseLight *= saturate(dot(normal, vMouthForward));

    vec3 color = baseColor.rgb * diffuseLight;
    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, 1.0);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));

    if (g_iRenderMode != 0)
    {
        vec3 tangent = normalize(cross(normal, vec3(0.0, 0.0, 1.0)));

        ApplySource1RenderModes(outputColor, baseColor.rgb, vec4(1.0), normal, eyeDirection, g_tColor, vTexCoordOut);
        ApplySource1NormalRenderModes(outputColor, normal, tangent, vec3(0.0, 0.0, 1.0), normal);
        ApplySource1LightingRenderModes(outputColor, diffuseLight, vec3(0.0));
    }
}
