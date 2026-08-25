#version 460

#ifndef VRF_VERTEX_FETCH_INCLUDE
#define VRF_VERTEX_FETCH_INCLUDE

#include "normal_decode.slang"

// A mesh shader has no vertex attributes, so it reads the vertex buffer itself. The buffer is bound as raw
// words and each attribute is described by a byte offset and a format code, both handed over as uniforms
// because they belong to the draw call rather than the geometry. Codes match VertexFetchFormat on the CPU.
#define VERTEX_FORMAT_NONE 0u
#define VERTEX_FORMAT_R32G32B32_FLOAT 1u
#define VERTEX_FORMAT_R32G32_FLOAT 2u
#define VERTEX_FORMAT_R32_UINT 3u
#define VERTEX_FORMAT_R16G16_FLOAT 4u
#define VERTEX_FORMAT_R16G16_SNORM 5u
#define VERTEX_FORMAT_R16G16_UNORM 6u

layout(std430, binding = 17) readonly buffer VertexFetchBuffer
{
    uint nVertexData[];
};

uniform uint nVertexStrideBytes;
uniform uvec2 vPositionAttribute; // byte offset, format code
uniform uvec2 vNormalAttribute;
uniform uvec2 vTexCoordAttribute;

// The offsets a mesh gives are byte offsets into an interleaved vertex, and every format below is four byte
// aligned within it, so a word index addresses them all.
uint LoadVertexWord(uint nVertex, uint nByteOffset, uint nWord)
{
    return nVertexData[((nVertex * nVertexStrideBytes + nByteOffset) >> 2u) + nWord];
}

vec4 FetchVertexAttribute(uint nVertex, uvec2 vAttribute)
{
    uint nOffset = vAttribute.x;
    uint nFormat = vAttribute.y;

    if (nFormat == VERTEX_FORMAT_R32G32B32_FLOAT)
    {
        return vec4(
            uintBitsToFloat(LoadVertexWord(nVertex, nOffset, 0u)),
            uintBitsToFloat(LoadVertexWord(nVertex, nOffset, 1u)),
            uintBitsToFloat(LoadVertexWord(nVertex, nOffset, 2u)),
            1.0);
    }

    if (nFormat == VERTEX_FORMAT_R32G32_FLOAT)
    {
        return vec4(
            uintBitsToFloat(LoadVertexWord(nVertex, nOffset, 0u)),
            uintBitsToFloat(LoadVertexWord(nVertex, nOffset, 1u)),
            0.0, 1.0);
    }

    if (nFormat == VERTEX_FORMAT_R16G16_FLOAT)
    {
        return vec4(unpackHalf2x16(LoadVertexWord(nVertex, nOffset, 0u)), 0.0, 1.0);
    }

    if (nFormat == VERTEX_FORMAT_R16G16_SNORM)
    {
        return vec4(unpackSnorm2x16(LoadVertexWord(nVertex, nOffset, 0u)), 0.0, 1.0);
    }

    if (nFormat == VERTEX_FORMAT_R16G16_UNORM)
    {
        return vec4(unpackUnorm2x16(LoadVertexWord(nVertex, nOffset, 0u)), 0.0, 1.0);
    }

    return vec4(0.0, 0.0, 0.0, 1.0);
}

vec3 FetchVertexPosition(uint nVertex)
{
    return FetchVertexAttribute(nVertex, vPositionAttribute).xyz;
}

vec2 FetchVertexTexCoord(uint nVertex)
{
    return FetchVertexAttribute(nVertex, vTexCoordAttribute).xy;
}

vec3 FetchVertexNormal(uint nVertex)
{
    if (vNormalAttribute.y != VERTEX_FORMAT_R32_UINT)
    {
        return vec3(0.0, 0.0, 1.0);
    }

    vec3 vNormal;
    vec4 vTangent;
    DecompressNormalTangent2(LoadVertexWord(nVertex, vNormalAttribute.x, 0u), vNormal, vTangent);
    return vNormal;
}

#endif
