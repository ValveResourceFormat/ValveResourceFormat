#version 460

#include "unlitgeneric_features.slang"
#include "common_vs.slang"

in vec3 vNORMAL;
in vec4 vTANGENT;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;

#if (D_BAKED_LIGHTING == 1)
    in vec4 vCOLOR1;
    out vec3 vBakedLightOut;
#endif

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, vNORMAL);
    vTangentOut = TransformTangentToWorld(objectToWorld, vTANGENT);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);

    vVertexColorOut = GetSource1VertexColor();

#if (D_BAKED_LIGHTING == 1)
    vBakedLightOut = vCOLOR1.rgb;
#endif
}
