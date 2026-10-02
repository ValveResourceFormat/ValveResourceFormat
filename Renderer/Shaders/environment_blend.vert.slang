#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "environment_blend_features.slang"

#include "common/instancing.slang"
#include "common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "common/compression.slang"

in vec2 vTEXCOORD1;
in vec4 vTEXCOORD4; // VertexPaintBlendParams
in vec4 vCOLOR;     // VertexPaintTintColor

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
out vec4 vTexCoord; // UV1 in xy, UV2 in zw
centroid out vec4 vVertexColorOut;
out vec3 vColorBlendValues; // layer 2 and 3 reveal in xy, environment_simple's roughness, metalness and self illumination in xyz

#if (WORLD_OVERLAY_OBJECT_SPACE == 1)
    out vec3 vPositionOs;
#endif

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/texcoord_scale_by_model.slang"

void main()
{
    ObjectData_t object = GetObjectData();

    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    ComputeWorldTangentFrame(skinTransform, vNormalOut, vTangentOut, vBitangentOut);
    vCentroidNormalOut = vNormalOut;

    #if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
        vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
    #elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
        vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
        vPerVertexLightingOut = pow2(Light);
    #endif

    vec4 tint = vec4(SrgbGammaToLinear(object.vTint.rgb), object.vTint.a);
    #if (GameVfx_environment_simple != 0)
        // How much of the model tint applies is painted per vertex.
        tint = mix(vec4(1.0), tint, vTEXCOORD4.w);
    #endif

    vec3 vertexPaint = mix(vec3(1.0), SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
    vVertexColorOut = vec4(tint.rgb * vertexPaint, tint.a);

    vTexCoord = ApplyTexCoordScaleByModel(vec4(vTEXCOORD.xy, vTEXCOORD1.xy), CalculateObjectToWorldMatrix());
    vColorBlendValues = vTEXCOORD4.xyz;

    #if (WORLD_OVERLAY_OBJECT_SPACE == 1)
        vPositionOs = vPOSITION;
    #endif
}
