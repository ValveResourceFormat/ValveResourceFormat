#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "common/compression.slang"

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;
flat out vec4 vTintColorFadeOut;

#include "common/ViewConstants.slang"

void main()
{
    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    ComputeWorldTangentFrame(skinTransform, vNormalOut, vTangentOut, vBitangentOut);

    vTintColorFadeOut = GetObjectTintSrgb();

    vTexCoordOut = vTEXCOORD;
}
