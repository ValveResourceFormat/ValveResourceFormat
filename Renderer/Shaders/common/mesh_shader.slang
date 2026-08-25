#version 460

#ifndef VRF_MESH_SHADER_INCLUDE
#define VRF_MESH_SHADER_INCLUDE

// Mesh shading over the MSLT block. The extension directive is not written here: hoisted #extension lines
// ignore the preprocessor, so the shader loader emits the spelling the driver actually exposes and sets
// D_MESH_SHADER_EXT to say which one it picked.
//
// EXT is what Source 2 itself compiles against, NV is the only one OpenGL exposes today. The two differ in
// where the output limits are declared, how the vertex array is named, and how a primitive is written, so
// everything below is expressed once and mapped onto whichever is present.
#define D_MESH_SHADER_EXT 0

#define MESHLET_MAX_VERTICES 128
#define MESHLET_MAX_PRIMITIVES 128
#define MESHLET_GROUP_SIZE 32

layout(local_size_x = MESHLET_GROUP_SIZE) in;

#if (D_MESH_SHADER_EXT == 1)
    layout(triangles, max_vertices = MESHLET_MAX_VERTICES, max_primitives = MESHLET_MAX_PRIMITIVES) out;

    #define MeshVertexPosition(nVertex) gl_MeshVerticesEXT[nVertex].gl_Position

    void SetMeshletCounts(uint nVertices, uint nPrimitives)
    {
        SetMeshOutputsEXT(nVertices, nPrimitives);
    }

    void WriteMeshletTriangle(uint nPrimitive, uvec3 vIndices)
    {
        gl_PrimitiveTriangleIndicesEXT[nPrimitive] = vIndices;
    }
#else
    layout(triangles) out;
    layout(max_vertices = MESHLET_MAX_VERTICES, max_primitives = MESHLET_MAX_PRIMITIVES) out;

    #define MeshVertexPosition(nVertex) gl_MeshVerticesNV[nVertex].gl_Position

    void SetMeshletCounts(uint nVertices, uint nPrimitives)
    {
        // NV has no vertex count to set, the highest written slot is what gets used
        gl_PrimitiveCountNV = nPrimitives;
    }

    void WriteMeshletTriangle(uint nPrimitive, uvec3 vIndices)
    {
        gl_PrimitiveIndicesNV[nPrimitive * 3u + 0u] = vIndices.x;
        gl_PrimitiveIndicesNV[nPrimitive * 3u + 1u] = vIndices.y;
        gl_PrimitiveIndicesNV[nPrimitive * 3u + 2u] = vIndices.z;
    }
#endif

// One meshlet of the MSLT block, mirroring MeshletDescriptor on the CPU side.
struct MeshletDescriptor
{
    uint nEntryOffset;
    uint nVertexOffset;
    uint nVertexCount;
    uint nTriangleCount;
};

layout(std430, binding = 15) readonly buffer MeshletPackedIndicesBuffer
{
    uint nMeshletEntries[];
};

layout(std430, binding = 16) readonly buffer MeshletDescriptorBuffer
{
    MeshletDescriptor meshletDescriptors[];
};

// A meshlet spans nVertexCount consecutive entries. The high 14 bits of each entry are the meshlet's vertex
// list, the low 18 bits are three 6-bit references making up one triangle, present in the first
// nTriangleCount entries only.
uint GetMeshletVertexListEntry(MeshletDescriptor meshlet, uint nLocalVertex)
{
    return nMeshletEntries[meshlet.nEntryOffset + nLocalVertex] >> 18u;
}

uvec3 GetMeshletTriangleReferences(MeshletDescriptor meshlet, uint nTriangle)
{
    uint nPacked = nMeshletEntries[meshlet.nEntryOffset + nTriangle] & 0x3FFFFu;
    return uvec3(nPacked & 0x3Fu, (nPacked >> 6u) & 0x3Fu, (nPacked >> 12u) & 0x3Fu);
}

// Resolves one 6-bit reference against a 64 entry sliding window. A reference equal to the next vertex in
// introduction order introduces it, anything else names the vertex congruent to it within the window, which
// lets a meshlet hold more than 64 vertices. Mirrors MeshletBuffer.DecodeMeshlet, integer division included:
// it truncates towards zero in GLSL just as it does in C#.
int ResolveMeshletReference(int nReference, inout int nMaxIntroduced)
{
    if (nReference == ((nMaxIntroduced + 1) & 0x3F))
    {
        return ++nMaxIntroduced;
    }

    return nReference + 64 * ((nMaxIntroduced - nReference) / 64);
}

// Writes the meshlet's triangles as local vertex indices. Up to 64 vertices every reference resolves to
// itself, because a window that never wraps leaves both branches above equal to the reference, so the whole
// stream decodes in parallel. Past that the window's state carries from one triangle to the next and the
// stream has to be walked in order, which one invocation does for the group.
void DecodeMeshletTriangles(MeshletDescriptor meshlet, uint nPrimitives)
{
    if (meshlet.nVertexCount <= 64u)
    {
        for (uint nTriangle = gl_LocalInvocationIndex; nTriangle < nPrimitives; nTriangle += MESHLET_GROUP_SIZE)
        {
            WriteMeshletTriangle(nTriangle, GetMeshletTriangleReferences(meshlet, nTriangle));
        }

        return;
    }

    if (gl_LocalInvocationIndex != 0u)
    {
        return;
    }

    int nMaxIntroduced = -1;

    for (uint nTriangle = 0u; nTriangle < nPrimitives; nTriangle++)
    {
        uvec3 vReferences = GetMeshletTriangleReferences(meshlet, nTriangle);

        uvec3 vIndices;
        vIndices.x = uint(ResolveMeshletReference(int(vReferences.x), nMaxIntroduced));
        vIndices.y = uint(ResolveMeshletReference(int(vReferences.y), nMaxIntroduced));
        vIndices.z = uint(ResolveMeshletReference(int(vReferences.z), nMaxIntroduced));

        WriteMeshletTriangle(nTriangle, vIndices);
    }
}

#endif
