#version 460

#include "sprite_features.slang"
#include "common_ps.slang"
#include "../common/depth.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;
flat in vec4 vGlowProxyOut;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture

uniform int F_ADDITIVE_BLEND;

// How much of a point that glows is in view, from taps around it on a plane facing the camera that are in front of the scene
float GetGlowVisibility(vec3 center, float radius)
{
    vec3 viewRight = cross(g_vCameraDirWs, g_vCameraUpDirWs);
    float centerDistance = distance(center, g_vCameraPositionWs);
    float visible = 0.0;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec3 tap = center + (radius * ((float(x) * viewRight) + (float(y) * g_vCameraUpDirWs)));
            vec4 clip = g_matWorldToProjection * vec4(tap, 1.0);
            vec2 screenCoord = (clip.xy / clip.w) * 0.5 + 0.5;

            if (clip.w <= 0.0 || any(lessThan(screenCoord, vec2(0.0))) || any(greaterThan(screenCoord, vec2(1.0))))
            {
                visible += 1.0;
                continue;
            }

            float sceneDepth = textureLod(g_tSceneDepth, screenCoord, 0.0).x;
            vec3 scenePosition = GetWorldPositionFromDepthValue(sceneDepth, tap - g_vCameraPositionWs);

            // The point is as far in front as its radius, so a bulb or a wall it sits against does not hide it
            if (distance(scenePosition, g_vCameraPositionWs) >= centerDistance - radius)
            {
                visible += 1.0;
            }
        }
    }

    return visible / 9.0;
}

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    vec3 color = baseColor.rgb * vVertexColorOut.rgb;

    if (vGlowProxyOut.w > 0.0)
    {
        color *= GetGlowVisibility(vGlowProxyOut.xyz, vGlowProxyOut.w);
    }

    // Additive sprites fade out in fog, they do not take its color
    if (F_ADDITIVE_BLEND != 0)
    {
        color *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(color, vFragPosition);
    }

    outputColor = vec4(color, baseColor.a * vVertexColorOut.a);}
