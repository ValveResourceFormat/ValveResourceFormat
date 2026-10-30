#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tDetail;

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);
    vec4 detailColor = SampleSource1Detail(g_tDetail, vTexCoordOut);
    baseColor = ApplySource1Detail(baseColor, detailColor);

    // The vertex colors are gamma space values
    vec3 vertexColor = SrgbGammaToLinear(vVertexColorOut.rgb);

    vec3 albedo = baseColor.rgb * g_vColorTint;
    vec3 color = albedo * vertexColor;
    color = ApplySource1DetailPostLighting(color, detailColor);
    color = ApplySource1SelfIllumFromMaskAlpha(color, albedo, g_tEnvMapMask, vTexCoordOut);

    if (F_ENVMAP != 0)
    {
        vec3 normal = GetSource1SurfaceNormal(vNormalOut, vFragPosition);
        vec3 envmapMask = GetSource1EnvmapMask(vTexCoordOut, baseColor.a, 1.0);
        color += GetSource1EnvmapColor(normal, vFragPosition, envmapMask, Source1TintGammaToLinear(g_vEnvMapTint));
    }

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, vVertexColorOut.a);

    outputColor = vec4(color, ApplySource1AlphaTest(alpha));
}
