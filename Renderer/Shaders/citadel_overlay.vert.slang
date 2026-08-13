#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "citadel_overlay_features.slang"

#include "common/instancing.slang"
#include "common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "common/compression.slang"

#if (F_DECAL_SPACE_NORMALS == 1)
    in vec4 vTEXCOORD5; // OverlayProjectionDirection
    out vec4 vProjectionDir;
#endif

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting; // COLOR1
    out vec3 vPerVertexLightingOut;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;
centroid out vec4 vVertexColorOut;

uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

void main()
{
    ObjectData_t object = GetObjectData();

    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    vNormalOut = TransformDirectionToWorld(skinTransform, normal);
    vTangentOut = TransformDirectionToWorld(skinTransform, tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);
    vCentroidNormalOut = vNormalOut;

    #if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
        vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
    #elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
        vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
        vPerVertexLightingOut = pow2(Light);
    #endif

    vTexCoordOut = vTEXCOORD.xy;

    vVertexColorOut.rgb = mix(vec3(1.0), SrgbGammaToLinear(object.vTint.rgb), g_flModelTintAmount);
    vVertexColorOut.a = object.vTint.a;

    #if (F_DECAL_SPACE_NORMALS == 1)
        vProjectionDir = vTEXCOORD5 * 2.0 - 1.0;
    #endif
}
