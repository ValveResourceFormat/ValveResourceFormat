#version 460

#include "../common/utils.slang"
#include "../common/instancing.slang"
#include "../common/animation.slang"

#define F_TWO_LAYER_BLEND 0
#define F_WORLDSPACE_UVS 0

in vec3 vPOSITION;
#include "../common/compression.slang"
in vec2 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;
in vec4 vTEXCOORD3;

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec4 vTangentOut;

out vec4 vBlendWeights;
out vec4 vBlendAlphas;
out vec4 vVertexColor;

out vec2 vTexCoordOut;
out vec2 vTexCoord1Out;
#if (F_TWO_LAYER_BLEND == 0)
out vec2 vTexCoord2Out;
out vec2 vTexCoord3Out;
#endif

#include "../common/view_constants.slang"
#include "../common/skinned_vertex.slang"

uniform float g_flTexCoordScale0 = 1.0;
uniform float g_flTexCoordScale1 = 1.0;
uniform float g_flTexCoordScale2 = 1.0;
uniform float g_flTexCoordScale3 = 1.0;

uniform float g_flTexCoordRotate0;
uniform float g_flTexCoordRotate1;
uniform float g_flTexCoordRotate2;
uniform float g_flTexCoordRotate3;

uniform vec2 g_vTexCoordOffset0;
uniform vec2 g_vTexCoordOffset1;
uniform vec2 g_vTexCoordOffset2;
uniform vec2 g_vTexCoordOffset3;

uniform vec2 g_vTexCoordScroll0;
uniform vec2 g_vTexCoordScroll1;
uniform vec2 g_vTexCoordScroll2;
uniform vec2 g_vTexCoordScroll3;


vec2 getTexCoord(float scale, float rotation, vec2 offset, vec2 scroll) {

    vec2 totalOffset = (scroll.xy * g_flTime) + offset.xy;

    return RotateVector2D(vTEXCOORD, rotation, vec2(1.0 / scale), totalOffset);
}

void main()
{
    WriteSkinnedPositionAndTangentFrame();

    vTexCoordOut = getTexCoord(g_flTexCoordScale0, g_flTexCoordRotate0, g_vTexCoordOffset0, g_vTexCoordScroll0);
    vTexCoord1Out = getTexCoord(g_flTexCoordScale1, g_flTexCoordRotate1, g_vTexCoordOffset1, g_vTexCoordScroll1);
#if (F_TWO_LAYER_BLEND == 0)
    vTexCoord2Out = getTexCoord(g_flTexCoordScale2, g_flTexCoordRotate2, g_vTexCoordOffset2, g_vTexCoordScroll2);
    vTexCoord3Out = getTexCoord(g_flTexCoordScale3, g_flTexCoordRotate3, g_vTexCoordOffset3, g_vTexCoordScroll3);
#endif

    // vTEXCOORD1 holds the amounts of layers 1 to 3, vTEXCOORD2 their blend softness, vTEXCOORD3 the painted tint color
    vBlendWeights.xyz = vTEXCOORD1.xyz;
    vBlendWeights.w = 0.0;
    vBlendAlphas.xyz = max(vTEXCOORD2.xyz * 0.5, 0.0001);
    vBlendAlphas.w = 0.0;

    vec4 tint = GetObjectTintSrgb();
    vVertexColor.rgb = SrgbGammaToLinear(tint.rgb) * SrgbGammaToLinear(vTEXCOORD3.rgb);
    vVertexColor.a = tint.a;
}
