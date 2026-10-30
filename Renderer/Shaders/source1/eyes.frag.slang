#version 460

#include "eyes_features.slang"
#include "eyes_common_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tIris; // SrgbRead(true)

// Up direction of the eyeball. It is used as it is stored in the model, which is relative to the bone.
uniform vec3 g_vEyeUp = vec3(0.0, 0.0, 1.0);
uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);
    vec4 irisColor = texture(g_tIris, GetSource1IrisCoords(vFragPosition));

    // Flattens the sides of the eyeball so the vertex lighting does not darken them
    vec3 fromEye = vFragPosition - g_vEyeOrigin;
    float normalDotUp = -dot(fromEye, g_vEyeUp) * 0.5;
    vec3 normal = normalize(normalDotUp * g_vEyeUp + fromEye);

    vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 diffuseLight = GetSource1Ambient(normal);
    vec3 specularLight = vec3(0.0);
    vec3 rimLight = vec3(0.0);
    AddSource1SceneLights(diffuseLight, specularLight, rimLight, vFragPosition, normal, eyeDirection, 1.0, 0.0);
    diffuseLight = GetSource1FullbrightFallback(diffuseLight, normal);

    vec3 color = mix(baseColor.rgb, irisColor.rgb, irisColor.a) * g_vColorTint * diffuseLight;
    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, 1.0);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));
}
