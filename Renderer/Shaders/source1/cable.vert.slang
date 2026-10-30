#version 460

#include "cable_features.slang"
#include "common_vs.slang"

// The direction the cable runs in, and half of its width, negative on the other side of the cable
in vec4 vCableAxis;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;

void main()
{
    vec3 center = vec4(vPOSITION, 1.0) * CalculateSkinTransform();

    // The cable is a ribbon that is as wide as the cable and turns around its length to face the camera
    vec3 across = cross(vCableAxis.xyz, center - g_vCameraPositionWs);
    float acrossLength = length(across);
    across = acrossLength > 0.0001 ? across / acrossLength : vec3(0.0);

    vFragPosition = center + (across * vCableAxis.w);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
    vVertexColorOut = vCOLOR;
}
