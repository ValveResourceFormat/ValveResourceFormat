#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;

out vec2 vTexCoordOut;
out vec4 vTintOut;

#include "common/ViewConstants.slang"
#include "common/utils.slang"
#include "common/instancing.slang"

void main()
{
    vTexCoordOut = vTEXCOORD;

    vec4 vTintSrgb = GetObjectTintSrgb();
    vTintOut = vec4(SrgbGammaToLinear(vTintSrgb.rgb), vTintSrgb.a);

    gl_Position = g_matWorldToProjection * vec4(vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix(), 1.0);
}
