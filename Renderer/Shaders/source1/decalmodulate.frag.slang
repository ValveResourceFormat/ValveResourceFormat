#version 460

#include "unlitgeneric_features.slang"
#include "common_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;

// Read as stored: the blend multiplies the framebuffer by this gamma space value twice, so 0.5 leaves it as it is
uniform sampler2D g_tColor; // Sampler(UserConfig) RepresentativeTexture

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    if (baseColor.a <= 0.0)
    {
        discard;
    }

    vec3 color = FadeSource1ModulateToNeutral(baseColor.rgb, vFragPosition);

    outputColor = vec4(color, 1.0);
}
