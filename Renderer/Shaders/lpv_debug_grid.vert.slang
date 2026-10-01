#version 460

// One quad per light probe sample, turned to the view and wide enough to hold the sphere traced in it
#include "common/instancing_base.slang"
#include "common/ViewConstants.slang"

// The sample's center in the volume's local space, one per instance
in vec3 vPOSITION;

out vec3 vFragPosition;
flat out vec3 vSampleCenter;
flat out uint g_nObjectIndex;

// Never written, the grid is lit from its probe; declared so the shared lighting links in every combo
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    centroid out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

// The instance index is taken by the samples, so the node is named by a uniform
uniform uint uObjectIndex = 0u;
uniform float g_flSampleSize = 10.0;

void main()
{
    InstanceDataStandard instanceData = g_instanceBuffer[uObjectIndex];

    vec3 center = vec4(vPOSITION, 1.0) * g_transformBuffer[instanceData.TransformIndex];

    vec2 corner = vec2(
        (float(gl_VertexID / 2) * 2.0) - 1.0,
        1.0 - (float(gl_VertexID % 2) * 2.0));

    vec3 offsetVs = vec3(corner.x, -corner.y, 0.0) * g_flSampleSize * 1.666;
    vec3 position = center + transpose(mat3(g_matWorldToView)) * offsetVs;

    vFragPosition = position;
    vSampleCenter = center;
    g_nObjectIndex = instanceData.Identification;
    gl_Position = g_matWorldToProjection * vec4(position, 1.0);
}
