#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tNormal;
uniform sampler2D g_tSceneColor; // reserved

uniform int F_BUMPMAP;
uniform int F_FADE_OUT_ON_SILHOUETTE;
uniform vec3 g_vRefractTint = vec3(1.0); // SrgbRead(true)
uniform float g_flRefractAmount = 0.0;

void main()
{
    vec3 cameraRay = vFragPosition - g_vCameraPositionWs;
    vec3 eyeDirection = normalize(-cameraRay);

    vec3 geometricNormal = normalize(cross(dFdx(vFragPosition), dFdy(vFragPosition)));
    geometricNormal *= sign(dot(geometricNormal, eyeDirection));

    vec4 normalTexel = vec4(0.5, 0.5, 1.0, 1.0);
    vec3 normal = geometricNormal;

    if (F_BUMPMAP != 0)
    {
        normalTexel = texture(g_tNormal, vTexCoordOut);
        mat3 tangentFrame = GetSource1TangentFrame(geometricNormal, vec4(0.0), vFragPosition, vTexCoordOut);
        normal = normalize(tangentFrame * (normalTexel.xyz * 2.0 - 1.0));
    }

    // The refraction fades out toward the silhouette of the surface
    float blend = 1.0;

    if (F_FADE_OUT_ON_SILHOUETTE != 0)
    {
        blend = saturate(dot(eyeDirection, geometricNormal));
        blend = blend * blend * blend;
    }

    vec2 screenCoord = gl_FragCoord.xy / vec2(textureSize(g_tSceneColor, 0));
    vec2 normalXY = normalTexel.xy * 2.0 - 1.0;

    // The screen coordinate runs upward
    vec2 warpOffset = vec2(normalXY.x, -normalXY.y) * normalTexel.a * g_flRefractAmount;
    vec2 warpCoord = saturate(screenCoord + warpOffset);

    vec3 colorNoWarp = textureLod(g_tSceneColor, screenCoord, 0.0).rgb;
    vec3 colorWarp = textureLod(g_tSceneColor, warpCoord, 0.0).rgb * g_vRefractTint;
    vec3 color = mix(colorNoWarp, colorWarp, blend);

    if (F_ENVMAP != 0)
    {
        vec3 reflection = SampleSource1Envmap(normal, vFragPosition);
        vec3 environment = reflection * normalTexel.a * Source1TintGammaToLinear(g_vEnvMapTint);

        environment = mix(environment, environment * environment, g_flEnvMapContrast);
        vec3 grey = vec3(dot(environment, vec3(0.299, 0.587, 0.114)));
        color += mix(grey, environment, g_flEnvMapSaturation);
    }

    float alpha = F_TRANSLUCENT != 0 ? normalTexel.a : 1.0;
    outputColor = vec4(color, alpha);
}
