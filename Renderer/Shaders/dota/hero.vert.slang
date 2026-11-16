#version 460

#define F_DETAIL 0

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
#if F_DETAIL
out vec2 vDetailTexCoordOut;
#endif
flat out vec4 vTintColorFadeOut;

#include "../common/view_constants.slang"
#include "../common/skinned_vertex.slang"

uniform float g_flDiffuseModulationAmount = 1.0;

#if F_DETAIL
uniform vec2 g_vDetailTexCoordScale = vec2(1.0);
uniform float g_flDetailTexCoordRotation; // degrees
uniform vec2 g_vDetailTexCoordOffset;

// Rotate and scale the detail UVs about the center of the texture
vec2 CalculateDetailTexCoord(vec2 texCoord)
{
    return RotateVector2D(texCoord, g_flDetailTexCoordRotation, g_vDetailTexCoordScale, g_vDetailTexCoordOffset);
}
#endif

void main()
{
    WriteSkinnedPositionAndTangentFrame();

    vec4 tint = GetObjectTintSrgb();
    vec3 linearTint = mix(vec3(1.0), SrgbGammaToLinear(tint.rgb), g_flDiffuseModulationAmount);
    vTintColorFadeOut = vec4(linearTint, tint.a);

    vTexCoordOut = vTEXCOORD;

#if F_DETAIL
    vDetailTexCoordOut = CalculateDetailTexCoord(vTEXCOORD);
#endif
}
