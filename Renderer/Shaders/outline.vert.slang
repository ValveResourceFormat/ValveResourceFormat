#version 460

#include "common/ViewConstants.slang"

#define F_ALPHA_TEST 0 // most shaders

in vec3 vPOSITION;
#if (F_ALPHA_TEST == 1)
    in vec2 vTEXCOORD;
    layout (location = 0) out vec2 texCoord;
#endif

#include "common/instancing.slang"
#include "common/animation.slang"

void main(void)
{
    #if (F_ALPHA_TEST == 1)
        texCoord = vTEXCOORD;
    #endif

    gl_Position = g_matWorldToProjection * vec4(vec4(vPOSITION, 1.0) * CalculateSkinTransform(), 1.0);
}
