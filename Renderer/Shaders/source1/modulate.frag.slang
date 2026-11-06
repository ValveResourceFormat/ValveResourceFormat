#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

// Read as stored: the blend multiplies the framebuffer by this gamma space value, so 0.5 is the neutral gray of the 2x blend
uniform sampler2D g_tColor; // Sampler(UserConfig) RepresentativeTexture

uniform vec3 g_vColorTint = vec3(1.0);

void main()
{
    vec4 modulation = vec4(g_vColorTint, g_flOpacityScale);
    vec4 color = saturate(texture(g_tColor, vTexCoordOut) * vVertexColorOut * modulation);

    // A transparent texel fades to gray
    color.rgb = mix(vec3(0.5), color.rgb, color.a);
    color.rgb = FadeSource1ModulateToNeutral(color.rgb, vFragPosition);

    outputColor = vec4(color.rgb, 1.0);
}
