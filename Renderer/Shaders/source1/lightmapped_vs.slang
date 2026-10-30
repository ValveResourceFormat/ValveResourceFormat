#version 460

#include "common_vs.slang"

in vec2 vLightmapUV;
in vec3 vNORMAL;
in vec4 vTANGENT;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;
out vec2 vLightmapUVOut;
out vec4 vVertexColorOut;
out float vBlendOut;

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, vNORMAL);
    vTangentOut = TransformTangentToWorld(objectToWorld, vTANGENT);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
    vLightmapUVOut = vLightmapUV;

    // The vertex alpha is the opacity only with the vertex color, and the blend factor when the surface blends
    vVertexColorOut = vec4(1.0);
    vBlendOut = 0.0;

#if (F_VERTEX_COLOR == 1)
    vVertexColorOut.rgb = vCOLOR.rgb;
#endif

#if (SOURCE1_BLEND_BY_VERTEX_ALPHA == 1)
    vBlendOut = vCOLOR.a;
#elif (F_VERTEX_COLOR == 1)
    vVertexColorOut.a = vCOLOR.a;
#endif
}
