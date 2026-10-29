#version 460

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec4 vCOLOR;
out vec4 vtxColor;
flat out vec3 vtxNormal;
out vec3 vtxPos;
out vec3 vtxPosWs;
out vec3 vtxNormalWs;

out vec3 camPos;

#include "common/view_constants.slang"
#include "common/instancing.slang"

// Lines along the shape's edges instead of its faces
#define D_LINES 0

// Fraction of their distance that lines move toward the camera
const float lineDepthPull = 5e-4;

void main(void) {
    vtxColor = vCOLOR;
    vtxNormal = vNORMAL;
    vtxPos = vPOSITION;

    camPos = g_vCameraPositionWs;

    mat3x4 objectToWorld = CalculateObjectToWorldMatrix();
    vtxPosWs = vec4(vPOSITION, 1.0) * objectToWorld;
    vtxNormalWs = vec4(vNORMAL, 0.0) * objectToWorld;

    gl_Position = g_matWorldToProjection * vec4(vtxPosWs, 1.0);

#if (D_LINES == 1)
    // Polygon offset does not apply to lines. Scaling reverse-Z depth pulls them toward the camera by a fraction
    // of their distance, which keeps up with the depth precision as it drops with distance.
    gl_Position.z *= 1.0 + lineDepthPull;
#endif
}
