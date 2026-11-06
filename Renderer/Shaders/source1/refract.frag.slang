#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "common_rendermodes_surface_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tSceneColor; // reserved

uniform int F_BUMPMAP;
uniform int F_FADE_OUT_ON_SILHOUETTE;
uniform vec3 g_vRefractTint = vec3(1.0);
uniform float g_flRefractAmount = 0.0;

void main()
{
    vec3 cameraRay = vFragPosition - g_vCameraPositionWs;
    vec3 eyeDirection = normalize(-cameraRay);

    vec3 geometricNormal = normalize(cross(dFdx(vFragPosition), dFdy(vFragPosition)));
    geometricNormal *= sign(dot(geometricNormal, eyeDirection));

    vec4 normalTexel = vec4(127.0 / 255.0, 127.0 / 255.0, 1.0, 1.0);
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
        color += GetSource1EnvmapColor(normal, vFragPosition, vec3(normalTexel.a), g_vEnvMapTint);
    }

    ApplyFog(color, vFragPosition);

    float alpha = F_TRANSLUCENT != 0 ? normalTexel.a : 1.0;
    outputColor = vec4(color, alpha);

    if (g_iRenderMode != 0)
    {
        vec3 tangent = GetSource1TangentFrame(geometricNormal, vec4(0.0), vFragPosition, vTexCoordOut)[0];
        vec3 bumpMap = normalTexel.xyz * 2.0 - 1.0;

        ApplySource1RenderModes(outputColor, g_vRefractTint, vec4(1.0), normal, eyeDirection, g_tNormal, vTexCoordOut);
        ApplySource1NormalRenderModes(outputColor, geometricNormal, tangent, bumpMap, normal);
        ApplySource1CubemapRenderMode(outputColor, normal, vFragPosition);
    }
}
