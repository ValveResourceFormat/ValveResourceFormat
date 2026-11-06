#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "common_rendermodes_ps.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

#if (SOURCE1_TWO_TEXTURE == 1)
in vec2 vTexCoord2Out;

uniform sampler2D g_tColor2; // SrgbRead(true) Sampler(UserConfig)
#endif

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tDetail; // Sampler(UserConfig)

uniform vec3 g_vColorTint = vec3(1.0);

#if (SOURCE1_MONITOR_SCREEN == 1)
// Blends from the color to its square, then from its gray to the color, in linear space
uniform float g_flMonitorContrast = 0.0;
uniform float g_flMonitorSaturation = 1.0;
uniform vec3 g_vMonitorTint = vec3(1.0);

vec3 ApplyMonitorColorAdjust(vec3 color)
{
    color = mix(color, color * color, g_flMonitorContrast);
    color = mix(vec3(dot(color, vec3(1.0 / 3.0))), color, g_flMonitorSaturation);
    return color * g_vMonitorTint;
}
#endif

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

#if (SOURCE1_MONITOR_SCREEN == 1)
    baseColor *= texture(g_tColor2, vTexCoord2Out);
#elif (SOURCE1_TWO_TEXTURE == 1)
    // The second texture multiplies the first, and the alpha of neither is the opacity
    baseColor.rgb *= texture(g_tColor2, vTexCoord2Out).rgb;
    baseColor.a = 1.0;
#endif

    vec4 detailColor = SampleSource1Detail(g_tDetail, vTexCoordOut);
    baseColor = ApplySource1Detail(baseColor, detailColor);

    // The vertex colors are gamma space values
    vec3 vertexColor = SrgbGammaToLinear(vVertexColorOut.rgb);

    vec3 albedo = baseColor.rgb * g_vColorTint;
    vec3 color = albedo * vertexColor;

#if (SOURCE1_MONITOR_SCREEN == 1)
    color = ApplyMonitorColorAdjust(color);
#endif
    color = ApplySource1DetailPostLighting(color, detailColor);
    color = ApplySource1SelfIllumFromMaskAlpha(color, albedo, g_tEnvMapMask, vTexCoordOut);

    vec3 normal = GetSource1SurfaceNormal(vNormalOut, vFragPosition);

    if (F_ENVMAP != 0)
    {
        vec3 envmapMask = GetSource1EnvmapMask(vTexCoordOut, baseColor.a, 1.0);
        color += GetSource1EnvmapColor(normal, vFragPosition, envmapMask, g_vEnvMapTint);
    }

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, vVertexColorOut.a);

    outputColor = vec4(color, ApplySource1AlphaTest(alpha));

    if (g_iRenderMode != 0)
    {
        vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);

        ApplySource1RenderModes(outputColor, albedo * vertexColor, vVertexColorOut, normal, eyeDirection, g_tColor, vTexCoordOut);
        ApplySource1CubemapRenderMode(outputColor, normal, vFragPosition);
    }
}
