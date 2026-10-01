#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec2 vTexCoordOut;
out vec4 vTintOut;
out vec3 vPositionWs;

#include "common/ViewConstants.slang"
#include "common/utils.slang"
#include "common/instancing.slang"

void main()
{
    vTexCoordOut = vTEXCOORD;

    vec4 vTintSrgb = GetObjectTintSrgb();
    vTintOut = vec4(SrgbGammaToLinear(vTintSrgb.rgb), vTintSrgb.a);

    vPositionWs = vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix();
    gl_Position = g_matWorldToProjection * vec4(vPositionWs, 1.0);
}
