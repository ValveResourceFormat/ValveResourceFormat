#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"
in vec4 vCOLOR;
in vec4 vTEXCOORD4;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec4 vColorBlendValues;

uniform float g_flWaterPlaneOffset;

#include "../common/shared_combos.slang"
#include "../common/baked_lighting_vs.slang"

#include "../common/view_constants.slang"
#include "../common/instancing.slang"

void main()
{
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(vNormalOut, tangent);
    vTangentOut = tangent;

    vec3 vPositionWs = vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix();

    // The water plane offset moves the surface along its normal and, in the plane, against the view direction
    vec3 vertDir = normalize(vPositionWs - g_vCameraPositionWs);
    vec2 parallaxOffset = vertDir.xy / -vertDir.z * 0.7;
    vPositionWs += (vNormalOut.xyz * g_flWaterPlaneOffset);
    vPositionWs.xy += ((-(normalize(parallaxOffset) * clamp(length(parallaxOffset), 0.0, 20.0))) * g_flWaterPlaneOffset);

    gl_Position = g_matWorldToProjection * vec4(vPositionWs, 1.0);
    vFragPosition = vPositionWs;

    WriteBakedLighting();

    vTexCoordOut = vTEXCOORD;
    vColorBlendValues = vTEXCOORD4;
}
