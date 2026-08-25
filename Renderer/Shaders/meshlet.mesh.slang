#version 460

#include "common/ViewConstants.slang"
#include "common/instancing_base.slang"
#include "common/mesh_shader.slang"
#include "common/vertex_fetch.slang"

uniform mat3x4 transform;
uniform uint nFirstMeshlet;
uniform uint nMeshletCount;
uniform uint nBaseInstance;
uniform int nBaseVertex;
uniform bool bIsInstancing = false;

// Per vertex outputs of a mesh shader are arrays, one slot per emitted vertex
layout (location = 0) out vec3 vFragPosition[];
layout (location = 1) out vec3 vNormalOut[];
layout (location = 2) out vec2 vTexCoordOut[];
layout (location = 3) flat out uint vMeshletIdOut[];

// A mesh shader draw carries no instance count of its own, so the instances are laid out along the workgroup
// axis instead: nMeshletCount groups per instance, one instance after the other.
mat3x4 GetObjectToWorldMatrix(uint nInstance)
{
    if (!bIsInstancing)
    {
        return transform;
    }

    return transforms[objects[nBaseInstance].TransformIndex + nInstance];
}

void main()
{
    uint nInstance = gl_WorkGroupID.x / nMeshletCount;
    uint nMeshlet = nFirstMeshlet + (gl_WorkGroupID.x - nInstance * nMeshletCount);

    MeshletDescriptor meshlet = meshletDescriptors[nMeshlet];
    mat3x4 objectToWorld = GetObjectToWorldMatrix(nInstance);

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
        vec3 vPositionWs = vec4(vPositionOs, 1.0) * objectToWorld;

        MeshVertexPosition(nLocal) = g_matWorldToProjection * vec4(vPositionWs, 1.0);

        vFragPosition[nLocal] = vPositionWs;
        vNormalOut[nLocal] = normalize(vec4(FetchVertexNormal(nVertex), 0.0) * objectToWorld);
        vTexCoordOut[nLocal] = FetchVertexTexCoord(nVertex);
        vMeshletIdOut[nLocal] = nMeshlet;
    }

    DecodeMeshletTriangles(meshlet, nPrimitives);
}
