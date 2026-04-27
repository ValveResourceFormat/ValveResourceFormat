#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/instancing.slang"
#include "common/animation.slang"
#include "common/morph.slang"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.slang"

centroid out vec4 vVertexColorOut;
out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;

uniform vec3 g_vColorTint = vec3(1.0);

uniform vec2 g_vTexCoordOffset;
uniform vec2 g_vTexCoordScale = vec2(1.0);
uniform vec2 g_vTexCoordScrollSpeed;
uniform vec2 g_vTexCoordCenter = vec2(0.5);
uniform float g_flTexCoordRotation = 0.0;

vec2 GetAnimatedUVs(vec2 texCoords)
{
    return RotateVector2D(texCoords, g_flTexCoordRotation, g_vTexCoordScale, g_vTexCoordOffset);
}

void main()
{
    ObjectData_t object = GetObjectData();

    mat4 skinTransform = object.transform * getSkinMatrix();
    vFragPosition = (skinTransform * vec4(vPOSITION + getMorphOffset(), 1.0)).xyz;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy);

    vVertexColorOut = SrgbGammaToLinear(object.vTint);

}
