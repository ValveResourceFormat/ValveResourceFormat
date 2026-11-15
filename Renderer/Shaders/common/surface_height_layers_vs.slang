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
uniform vec4 g_vTexCoordXform0_1 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vTexCoordXform1_1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec2 g_vTexCoordScale1 = vec2(1.0);

#if (F_DETAIL_NORMAL == 1)
    uniform int g_nDetailUVSet1 = -1; // -1=Inherit, 0=Biplanar, 1=UV1, 2=UV2
    uniform vec4 g_vDetailTexCoordXform0_1 = vec4(1.0, 0.0, 0.0, 0.0);
    uniform vec4 g_vDetailTexCoordXform1_1 = vec4(0.0, 1.0, 0.0, 0.0);
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);

/* Expressions
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec2 g_vTexCoordCenter1 = vec2(0.5);
uniform vec2 g_vTexCoordOffset1 = vec2(0.0);
uniform float g_flDetailTexCoordRotation1 = 0.0;
uniform vec2 g_vDetailTexCoordCenter1 = vec2(0.5);
uniform vec2 g_vDetailTexCoordOffset1 = vec2(0.0);

g_vTexCoordXform0_1 = TexCoordXform0(g_flTexCoordRotation1, g_vTexCoordScale1, g_vTexCoordCenter1, g_vTexCoordOffset1);
g_vTexCoordXform1_1 = TexCoordXform1(g_flTexCoordRotation1, g_vTexCoordScale1, g_vTexCoordCenter1, g_vTexCoordOffset1);
g_vDetailTexCoordXform0_1 = TexCoordXform0(g_flDetailTexCoordRotation1, g_vDetailTexCoordScale1, g_vDetailTexCoordCenter1, g_vDetailTexCoordOffset1);
g_vDetailTexCoordXform1_1 = TexCoordXform1(g_flDetailTexCoordRotation1, g_vDetailTexCoordScale1, g_vDetailTexCoordCenter1, g_vDetailTexCoordOffset1);
*/

    out vec2 vDetailTexCoords;
#endif

// The coordinates of a layer: the first or second UV set, turned and scaled around the center
vec2 GetLayerTexCoord(int uvSet, vec4 baseUvs, vec4 xform0, vec4 xform1)
{
    vec2 selected = (uvSet == 2) ? baseUvs.zw : baseUvs.xy;

    return ApplyTexCoordXform(selected, xform0, xform1);
}

// A detail texture follows its layer's UV set unless it names one of its own
vec2 GetDetailTexCoord(int detailUvSet, int layerUvSet, vec4 baseUvs, vec4 xform0, vec4 xform1)
{
    int uvSet = (detailUvSet == -1) ? layerUvSet : detailUvSet;

    return GetLayerTexCoord(uvSet, baseUvs, xform0, xform1);
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

    vTexCoordOut = GetLayerTexCoord(g_nUVSet1, baseUvs, g_vTexCoordXform0_1, g_vTexCoordXform1_1);

#if (F_DETAIL_NORMAL == 1)
    vDetailTexCoords = GetDetailTexCoord(g_nDetailUVSet1, g_nUVSet1, baseUvs, g_vDetailTexCoordXform0_1, g_vDetailTexCoordXform1_1);
#endif

    vColorBlendValues = vTEXCOORD4;
    vCentroidNormalOut = vNormalOut;

    return baseUvs;
}

#endif
