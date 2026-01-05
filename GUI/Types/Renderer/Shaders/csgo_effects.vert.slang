#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.slang"
in vec4 vCOLOR;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
centroid out vec4 vColorOut;

#include "common/ViewConstants.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

void main()
{
    mat4 skinTransform = CalculateObjectToWorldMatrix() * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);

    vTexCoordOut = vTEXCOORD;

    vColorOut = GetObjectTintSrgb();
    vColorOut.rgb = SrgbGammaToLinear(vColorOut.rgb);
    vColorOut.rgb *= g_vColorTint.rgb;

    if (vCOLOR != vec4(0.0)) // Is this necessary?
    {
        vColorOut *= vCOLOR;
    }
}
