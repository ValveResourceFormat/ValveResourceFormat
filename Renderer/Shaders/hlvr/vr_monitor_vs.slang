#version 460

// The entry file defines MONITOR_MODEL_UP_TANGENT when its game bends the glass around the up axis of the model.

#include "../common/utils.slang"
#include "../common/view_constants.slang"

#include "../common/instancing.slang"
#include "../common/animation.slang"
#include "../common/morph.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;

// Read by the shared material and lighting functions the pixel shader includes but never calls, which some drivers still link
centroid out vec4 vVertexColorOut;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    centroid out vec4 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

void main()
{
    mat3x4 skinTransform = CalculateSkinTransform();
    vFragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * skinTransform;

    vec3 normalWs;
    vec4 tangentWs;

#if (MONITOR_MODEL_UP_TANGENT)
    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);
    ComputeWorldTangentFrame(skinTransform, normal, vec4(0.0, 0.0, 1.0, 1.0), normalWs, tangentWs);

    vNormalOut = normalize(normalWs);
    vTangentOut = vec4(normalize(tangentWs.xyz), tangentWs.w);
#else
    ComputeWorldTangentFrame(skinTransform, normalWs, tangentWs);

    // The tangent of the mesh is made perpendicular to the normal, its w keeps the handedness
    vNormalOut = normalize(normalWs);
    vTangentOut = vec4(normalize(tangentWs.xyz - vNormalOut * dot(tangentWs.xyz, vNormalOut)), tangentWs.w);
#endif

    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
    vTexCoordOut = vTEXCOORD.xy;
    vVertexColorOut = vec4(1.0);

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec4(0.0);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vPerVertexLightingOut = vec3(0.0);
#endif
}
