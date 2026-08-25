#version 460

#include "common/ViewConstants.slang"
#include "common/mesh_shader.slang"
#include "common/vertex_fetch.slang"

uniform mat3x4 transform;
uniform uint nFirstMeshlet;
uniform int nBaseVertex;

// Per vertex outputs of a mesh shader are arrays, one slot per emitted vertex
layout (location = 0) out vec3 vFragPosition[];
layout (location = 1) out vec3 vNormalOut[];
layout (location = 2) out vec2 vTexCoordOut[];
layout (location = 3) flat out uint vMeshletIdOut[];

void main()
{
    uint nMeshlet = nFirstMeshlet + gl_WorkGroupID.x;
    MeshletDescriptor meshlet = meshletDescriptors[nMeshlet];

    uint nVertices = min(meshlet.nVertexCount, uint(MESHLET_MAX_VERTICES));
    uint nPrimitives = min(meshlet.nTriangleCount, uint(MESHLET_MAX_PRIMITIVES));

    if (gl_LocalInvocationIndex == 0u)
    {
        SetMeshletCounts(nVertices, nPrimitives);
    }

    for (uint nLocal = gl_LocalInvocationIndex; nLocal < nVertices; nLocal += MESHLET_GROUP_SIZE)
    {
        // The vertex list is meshlet relative, the meshlet's own offset places it in the vertex buffer
        uint nVertex = uint(nBaseVertex) + meshlet.nVertexOffset + GetMeshletVertexListEntry(meshlet, nLocal);

        vec3 vPositionOs = FetchVertexPosition(nVertex);
        vec3 vPositionWs = vec4(vPositionOs, 1.0) * transform;

        MeshVertexPosition(nLocal) = g_matWorldToProjection * vec4(vPositionWs, 1.0);

        vFragPosition[nLocal] = vPositionWs;
        vNormalOut[nLocal] = normalize(vec4(FetchVertexNormal(nVertex), 0.0) * transform);
        vTexCoordOut[nLocal] = FetchVertexTexCoord(nVertex);
        vMeshletIdOut[nLocal] = nMeshlet;
    }

    DecodeMeshletTriangles(meshlet, nPrimitives);
}
