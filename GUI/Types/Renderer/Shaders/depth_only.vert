#version 460

#include "common/ViewConstants.glsl"

#define D_ANIMATED 0
#define D_MORPHED 0
#define F_ALPHA_TEST 0

layout (location = 0) in vec3 vPOSITION;
#if (F_ALPHA_TEST == 1)
    layout (location = 3) in vec2 vTEXCOORD;
    layout (location = 0) out vec2 texCoord;
#endif
#include "common/animation.glsl"
#include "common/morph.glsl"

uniform mat4 transform;

void main()
{
    mat4 vertexTransform = transform;
    vec3 vertexPosition = vPOSITION;

    #if (D_ANIMATED == 1)
        vertexTransform *= getSkinMatrix();
    #endif

    #if (D_MORPHED == 1)
        vertexPosition += getMorphOffset();
    #endif

    #if (F_ALPHA_TEST == 1)
        texCoord = vTEXCOORD;
    #endif

    vec4 fragPosition = vertexTransform * vec4(vertexPosition, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
}
