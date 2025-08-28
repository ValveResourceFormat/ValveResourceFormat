#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"
in vec4 vCOLOR;
in vec4 vTEXCOORD4;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec4 vColorBlendValues;

uniform float g_flSkyBoxScale;
uniform float g_flWaterPlaneOffset;

#include "common/features.glsl"
#include "common/LightingConstants.glsl"

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;
    out vec3 vPerVertexLightingOut;
#endif

#include "common/ViewConstants.glsl"
#include "common/instancing.glsl"

#define F_REFRACTION 0

void main()
{
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(vNormalOut, tangent);
    vTangentOut = tangent.xyz;
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    vec4 vPositionWs = CalculateObjectToWorldMatrix() * vec4(vPOSITION, 1.0);

    //Here comes some of the greatest fucking bullshit I have ever seen
    vec3 vertDir = normalize(vPositionWs.xyz - g_vCameraPositionWs);
    vec2 parallaxOffset = vertDir.xy / -vertDir.z * 0.7; //like ???? what the fuck
    vPositionWs.xyz += (vNormalOut.xyz * g_flWaterPlaneOffset);
    vPositionWs.xy += ((-(normalize(parallaxOffset) * clamp(length(parallaxOffset), 0.0, 20.0))) * g_flWaterPlaneOffset);

    gl_Position = g_matWorldToProjection * vPositionWs;
    vFragPosition = vPositionWs.xyz;

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
        vPerVertexLightingOut = pow2(Light);
    #endif

    vTexCoordOut = vTEXCOORD;
    vColorBlendValues = vTEXCOORD4;
}
