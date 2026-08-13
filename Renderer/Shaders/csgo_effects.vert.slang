#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
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
    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    vNormalOut = TransformDirectionToWorld(skinTransform, normal);

    vTexCoordOut = vTEXCOORD;

    vColorOut = GetObjectTintSrgb();
    vColorOut.rgb = SrgbGammaToLinear(vColorOut.rgb);
    vColorOut.rgb *= g_vColorTint.rgb;

    if (vCOLOR != vec4(0.0)) // Is this necessary?
    {
        vColorOut *= vCOLOR;
    }
}
