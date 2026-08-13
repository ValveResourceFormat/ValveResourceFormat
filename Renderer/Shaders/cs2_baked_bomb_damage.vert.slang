#version 460

in vec3 vPOSITION;
in vec4 vCOLOR;
in vec2 vTEXCOORD;
in float vPHASE;

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

    vec3 worldPos = vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix();
    vtxWorldPos = worldPos;
    vtxClipPos = g_matWorldToProjection * vec4(worldPos, 1.0);
    gl_Position = vtxClipPos;
}
