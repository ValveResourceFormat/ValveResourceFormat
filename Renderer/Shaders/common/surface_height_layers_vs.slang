#version 460

#ifndef VRF_SURFACE_HEIGHT_LAYERS_VS_INCLUDE
#define VRF_SURFACE_HEIGHT_LAYERS_VS_INCLUDE

// Vertex side of the height layers of surface_height_layers.slang.
// The shader includes its features file (utils, instancing, animation and features first), then this, and writes main()
// from WriteHeightLayerVertex(). It reads F_DETAIL_NORMAL and D_BAKED_LIGHTING_FROM_*, so it declares those combos.

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "compression.slang"

in vec4 vCOLOR;
in vec2 vTEXCOORD1;

#include "baked_lighting_vs.slang"

in vec4 vTEXCOORD4; // default should be (0, 0, 0, 1)
out vec4 vColorBlendValues;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;
out vec4 vTintColor_ModelAmount;
centroid out vec4 vVertexColorOut;

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flModelTintAmount = 1.0;

#include "view_constants.slang"
#include "lighting_constants.slang"
#include "skinned_vertex.slang"

// Material 1
uniform int g_nUVSet1 = 1; // 0=Biplanar, 1=UV1, 2=UV2
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec2 g_vTexCoordCenter1 = vec2(0.5);
uniform vec2 g_vTexCoordOffset1 = vec2(0.0);
uniform vec2 g_vTexCoordScale1 = vec2(1.0);

#if (F_DETAIL_NORMAL == 1)
    uniform int g_nDetailUVSet1 = -1; // -1=Inherit, 0=Biplanar, 1=UV1, 2=UV2
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform vec2 g_vDetailTexCoordCenter1 = vec2(0.5);
    uniform vec2 g_vDetailTexCoordOffset1 = vec2(0.0);
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);

    out vec2 vDetailTexCoords;
#endif

// The coordinates of a layer: the first or second UV set, turned and scaled around the center
vec2 GetLayerTexCoord(int uvSet, vec4 baseUvs, float rotation, vec2 scale, vec2 offset, vec2 center)
{
    vec2 selected = (uvSet == 2) ? baseUvs.zw : baseUvs.xy;

    // The scale applies around the UV origin, only the rotation pivots on the center
    return RotateVector2D(scale * selected - center, rotation) + center + offset;
}

// A detail texture follows its layer's UV set unless it names one of its own
vec2 GetDetailTexCoord(int detailUvSet, int layerUvSet, vec4 baseUvs, float rotation, vec2 scale, vec2 offset, vec2 center)
{
    int uvSet = (detailUvSet == -1) ? layerUvSet : detailUvSet;

    return GetLayerTexCoord(uvSet, baseUvs, rotation, scale, offset, center);
}

// Writes the position, frame, baked lighting, tints and the coordinates of the first material.
// Returns the base UV sets for the coordinates of the other materials.
vec4 WriteHeightLayerVertex()
{
    ObjectData_t object = GetObjectData();

    WriteSkinnedPositionAndTangentFrame();

    WriteBakedLighting();

    vTintColor_ModelAmount.rgb = SrgbGammaToLinear(object.vTint.rgb);
    float flLowestPoint = Min3(vTintColor_ModelAmount.rgb);
    vTintColor_ModelAmount.a = g_flModelTintAmount * (1.0 - flLowestPoint);

    vec3 vertexPaint = mix(vec3(1.0), vCOLOR.rgb, vec3(vCOLOR.a));
    vVertexColorOut = vec4(g_vColorTint.rgb * vertexPaint, object.vTint.a);

    vec4 baseUvs = vec4(vTEXCOORD.xy, vTEXCOORD1.xy);

    vTexCoordOut = GetLayerTexCoord(g_nUVSet1, baseUvs, g_flTexCoordRotation1, g_vTexCoordScale1, g_vTexCoordOffset1, g_vTexCoordCenter1);

#if (F_DETAIL_NORMAL == 1)
    vDetailTexCoords = GetDetailTexCoord(g_nDetailUVSet1, g_nUVSet1, baseUvs, g_flDetailTexCoordRotation1, g_vDetailTexCoordScale1, g_vDetailTexCoordOffset1, g_vDetailTexCoordCenter1);
#endif

    vColorBlendValues = vTEXCOORD4;
    vCentroidNormalOut = vNormalOut;

    return baseUvs;
}

#endif
