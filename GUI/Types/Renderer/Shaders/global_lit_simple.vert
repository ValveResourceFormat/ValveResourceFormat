#version 460

#include "common/animation.glsl"

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_PAINT_VERTEX_COLORS 0
//End of parameter defines

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"

#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vTEXCOORD2;
    out vec4 vVertexColorOut;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;
out vec4 vTintColorFadeOut;

uniform vec2 g_vTexCoordOffset = vec2(0.0);
uniform vec2 g_vTexCoordScale = vec2(1.0);

#include "common/ViewConstants.glsl"
uniform mat4 transform;
uniform vec4 vTint;

void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    vTintColorFadeOut = vTint;
    vTexCoordOut = vTEXCOORD * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;

    #if (F_PAINT_VERTEX_COLORS == 1)
        vVertexColorOut = vTEXCOORD2;
    #endif
}
