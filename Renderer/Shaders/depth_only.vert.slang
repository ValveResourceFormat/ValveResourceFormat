#version 460

#include "common/ViewConstants.slang"

#define D_MORPHED 0
#define F_ALPHA_TEST 0 // most shaders

in vec3 vPOSITION;
#if (F_ALPHA_TEST == 1)
    in vec2 vTEXCOORD;
    layout (location = 0) out vec2 texCoord;
#endif

#include "common/instancing.slang"
#include "common/animation.slang"
#include "common/morph.slang"

void main()
{
    vec3 vertexPosition = vPOSITION;

    #if (D_MORPHED == 1)
        vertexPosition += getMorphOffset();
    #endif

    #if (F_ALPHA_TEST == 1)
        texCoord = vTEXCOORD;
    #endif

    vec3 fragPosition = vec4(vertexPosition, 1.0) * CalculateSkinTransform();

    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
}
