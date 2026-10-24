#version 460

in vec3 vPOSITION;
in vec4 vCOLOR;
in vec2 vHandleCorner;
in float vHandleSize;

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/instancing.slang"

out vec4 vColorOut;
out vec2 vTexCoord;
flat out vec4 vRectPx;

// A quad around the point, twice the size of the handle in pixels across, whatever the distance
void main(void)
{
    mat4 objectToWorld = UnpackMatrix4(CalculateObjectToWorldMatrix());
    vec4 centerPs = g_matWorldToProjection * (objectToWorld * vec4(vPOSITION, 1.0));

    vec2 extentPs = g_vInvViewportSize * (2.0 * vHandleSize) * centerPs.w;

    vec4 positionPs = centerPs;
    positionPs.xy += vHandleCorner * extentPs;

    // The edges of the quad in window pixels, which the border is drawn along
    vec2 centerNdc = centerPs.xy / centerPs.w;
    vec2 extentNdc = g_vInvViewportSize * (2.0 * vHandleSize);
    vec2 minPx = ((centerNdc - extentNdc) * 0.5 + 0.5) / g_vInvViewportSize;
    vec2 maxPx = ((centerNdc + extentNdc) * 0.5 + 0.5) / g_vInvViewportSize;

    vColorOut = vec4(SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
    vTexCoord = vec2(vHandleCorner.x * 0.5 + 0.5, 0.5 - vHandleCorner.y * 0.5);
    vRectPx = vec4(minPx, maxPx);

    gl_Position = positionPs;
}
