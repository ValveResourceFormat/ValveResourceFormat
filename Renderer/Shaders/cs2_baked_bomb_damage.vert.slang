#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 1) in vec4 vCOLOR;
layout (location = 2) in vec2 vTEXCOORD;
layout (location = 3) in float vPHASE;

out vec4 vtxColor;
out vec2 vtxUv;
out vec4 vtxClipPos;
out vec3 vtxWorldPos;
out float vtxPhase;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    vtxUv = vTEXCOORD;
    vtxPhase = vPHASE;

    vec4 worldPos = CalculateObjectToWorldMatrix() * vec4(vPOSITION, 1.0);
    vtxWorldPos = worldPos.xyz;
    vtxClipPos = g_matWorldToProjection * worldPos;
    gl_Position = vtxClipPos;
}
