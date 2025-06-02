#version 460

#include "common/utils.glsl"
#include "common/animation.glsl"

#define F_WORLDSPACE_UVS 0

layout (location = 0) in vec3 vPOSITION;
#include "common/compression.glsl"
layout (location = 6) in vec2 vTEXCOORD;
layout (location = 7) in vec4 vTEXCOORD1;
layout (location = 8) in vec4 vTEXCOORD2;
layout (location = 9) in vec4 vTEXCOORD3;

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec4 vBlendWeights;
out vec4 vBlendAlphas;
out vec4 vVertexColor;

out vec2 vTexCoordOut;
out vec2 vTexCoord1Out;
#if (F_TWO_LAYER_BLEND == 0)
out vec2 vTexCoord2Out;
out vec2 vTexCoord3Out;
#endif

#include "common/ViewConstants.glsl"
uniform mat4 transform;
uniform vec4 vTint = vec4(1.0);

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

    //Transform degrees to radians
    float r = radians(rotation);

    vec2 totalOffset = (scroll.xy * g_flTime) + offset.xy;

    //Scale texture
    vec2 coord = vTEXCOORD - vec2(0.5);

    float SinR = sin(r);
    float CosR = cos(r);

    //Rotate vector
    vec2 rotatedCoords = vec2(CosR * vTEXCOORD.x - SinR * vTEXCOORD.y,
        SinR * vTEXCOORD.x + CosR * vTEXCOORD.y);

    return (rotatedCoords / scale) + vec2(0.5) + totalOffset;
}

void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    vTexCoordOut = getTexCoord(g_flTexCoordScale0, g_flTexCoordRotate0, g_vTexCoordOffset0, g_vTexCoordScroll0);
    vTexCoord1Out = getTexCoord(g_flTexCoordScale1, g_flTexCoordRotate1, g_vTexCoordOffset1, g_vTexCoordScroll1);
    vTexCoord2Out = getTexCoord(g_flTexCoordScale2, g_flTexCoordRotate2, g_vTexCoordOffset2, g_vTexCoordScroll2);
    vTexCoord3Out = getTexCoord(g_flTexCoordScale3, g_flTexCoordRotate3, g_vTexCoordOffset3, g_vTexCoordScroll3);


    //vTEXCOORD1 - (X,Y,Z) - tex1, 2, and 3 blend softness (not working right now), W - reserved for worldspace uvs
    //vTEXCOORD2 - Painted tint color
    //vTEXCOORD3 - X - amount of tex1, Y - amount of tex2, Z - amount of tex3, W - reserved for worldspace uvs

    vBlendWeights.xyz = vTEXCOORD1.xyz;
    vBlendWeights.w = 0.0;
    vBlendAlphas.xyz = vTEXCOORD2.xyz;//max(vTEXCOORD1.xyz * 0.5, 1e-6);
    vBlendAlphas.w = 0.0;

    vVertexColor.rgb = SrgbGammaToLinear(vTint.rgb) * SrgbGammaToLinear(vTEXCOORD3.rgb);
    vVertexColor.a = vTint.a;
}
