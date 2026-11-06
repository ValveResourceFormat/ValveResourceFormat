#version 460

#include "cable_features.slang"
#include "common_ps.slang"
#include "common_rendermodes_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    // The normal map shades the round of the cable, lit from the front like a half lambert
    vec3 normal = texture(g_tNormal, vTexCoordOut).rgb * 2.0 - 1.0;
    float shade = normal.z * 0.5 + 0.5;
    shade *= shade;

    vec3 color = baseColor.rgb * vVertexColorOut.rgb * shade;
    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, vVertexColorOut.a);

    outputColor = vec4(color, ApplySource1AlphaTest(alpha));

    if (g_iRenderMode != 0)
    {
        vec3 facing = vec3(0.0, 0.0, 1.0);
        ApplySource1RenderModes(outputColor, baseColor.rgb * vVertexColorOut.rgb, vVertexColorOut, facing, facing, g_tColor, vTexCoordOut);
    }
}
