#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/instancing.slang"
#include "common/animation.slang"
#include "common/morph.slang"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.slang"

#define F_TEXTURE_ANIMATION 0
uniform int F_TEXTURE_ANIMATION_MODE;

#define F_BLEND 0

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    centroid out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vCOLOR1;
    out vec3 vPerVertexLightingOut;
#endif

#if (F_BLEND > 0)
    in vec4 vTEXCOORD2;
    out vec4 vColorBlendValues;
#endif

centroid out vec4 vVertexColorOut;
out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;

uniform vec3 g_vColorTint = vec3(1.0);

uniform vec2 g_vTexCoordOffset;
uniform vec2 g_vTexCoordScale = vec2(1.0);
uniform vec2 g_vTexCoordScrollSpeed;
uniform vec2 g_vTexCoordCenter = vec2(0.5);
uniform float g_flTexCoordRotation = 0.0;

#if (F_TEXTURE_ANIMATION == 1)
    uniform vec2 g_vAnimationGrid = vec2(1, 1);
    uniform int g_nNumAnimationCells = 1;
    uniform float g_flAnimationTimePerFrame;
    uniform float g_flAnimationTimeOffset;
    uniform float g_flAnimationFrame;
#endif

vec2 GetAnimatedUVs(vec2 texCoords)
{
    #if (F_TEXTURE_ANIMATION == 1)
        uint frame = uint(g_flAnimationFrame);
        uint cells = uint(g_nNumAnimationCells);
        if (F_TEXTURE_ANIMATION_MODE == 0)
            frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % cells;
        if (F_TEXTURE_ANIMATION_MODE == 1 && (cells > (frame + 1u)))
            frame = min(frame, cells - 1u);

        uint col = frame % uint(g_vAnimationGrid.x);
        uint row = frame / uint(g_vAnimationGrid.x);
        texCoords.x = (texCoords.x + col) / g_vAnimationGrid.x;
        texCoords.y = (texCoords.y + row) / g_vAnimationGrid.y;
    #endif

    return RotateVector2D(texCoords, g_flTexCoordRotation, g_vTexCoordScale, g_vTexCoordOffset);
}

void main()
{
    ObjectData_t object = GetObjectData();

    mat4 skinTransform = object.transform * getSkinMatrix();
    vFragPosition = (skinTransform * vec4(vPOSITION + getMorphOffset(), 1.0)).xyz;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy);

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vec4 vPerVertexLighting = vCOLOR1;
    vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    vPerVertexLightingOut = pow2(Light);
#endif

    vVertexColorOut = SrgbGammaToLinear(object.vTint);

#if (F_BLEND > 0)
    vColorBlendValues = vTEXCOORD2;
#endif
}
