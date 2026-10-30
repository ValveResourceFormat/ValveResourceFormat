#version 460

#include "detailsprite_features.slang"
#include "common_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    vec3 color = baseColor.rgb * g_vColorTint * vVertexColorOut.rgb;
    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseColor.a, vVertexColorOut.a);

    outputColor = vec4(color, ApplySource1AlphaTest(alpha));
}
