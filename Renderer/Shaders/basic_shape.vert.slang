#version 460

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec4 vCOLOR;
out vec4 vtxColor;
out vec3 vtxNormal;
out vec3 vtxPos;
out vec3 vtxPosWs;
out vec3 vtxNormalWs;

out vec3 camPos;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    vtxNormal = vNORMAL;
    vtxPos = vPOSITION;

    camPos = g_vCameraPositionWs;

    mat3x4 objectToWorld = CalculateObjectToWorldMatrix();
    vtxPosWs = vec4(vPOSITION, 1.0) * objectToWorld;
    vtxNormalWs = vec4(vNORMAL, 0.0) * objectToWorld;

    gl_Position = g_matWorldToProjection * vec4(vtxPosWs, 1.0);
}
