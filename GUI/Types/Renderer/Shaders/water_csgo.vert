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

#include "common/features.glsl"

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;

    #include "common/LightingConstants.glsl"
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;
    out vec3 vPerVertexLightingOut;
#endif

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    vec4 fragPosition = transform * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    vec4 tangent;
    GetOptionallyCompressedNormalTangent(vNormalOut, tangent);
    vTangentOut = tangent.xyz;
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
        vPerVertexLightingOut = pow2(Light);
    #endif

    vTexCoordOut = vTEXCOORD;
    vColorBlendValues = vTEXCOORD4;
}
