#version 460

#include "../common/utils.slang"
#include "../common/instancing.slang"
#include "../common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;
flat out vec4 vTintColorFadeOut;

#include "../common/view_constants.slang"
#include "../common/skinned_vertex.slang"

uniform float g_flDiffuseModulationAmount = 1.0;

void main()
{
    WriteSkinnedPositionAndTangentFrame();

    vec4 tint = GetObjectTintSrgb();
    vec3 linearTint = mix(vec3(1.0), SrgbGammaToLinear(tint.rgb), g_flDiffuseModulationAmount);
    vTintColorFadeOut = vec4(linearTint, tint.a);

    vTexCoordOut = vTEXCOORD;
}
