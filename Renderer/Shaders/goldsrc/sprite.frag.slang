#version 460

#include "../common/utils.slang"
#include "../common/view_constants.slang"
#include "../common/fog.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

uniform int F_ADDITIVE_BLEND;

// Only the alpha of the texture is used, the color is the one of the vertices
uniform int F_COLOR_ONLY;

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);
    vec3 color = F_COLOR_ONLY != 0 ? vVertexColorOut.rgb : baseColor.rgb * vVertexColorOut.rgb;

    // Additive sprites fade out in fog, they do not take its color
    if (F_ADDITIVE_BLEND != 0)
    {
        color *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(color, vFragPosition);
    }

    outputColor = vec4(color, baseColor.a * vVertexColorOut.a);
}
