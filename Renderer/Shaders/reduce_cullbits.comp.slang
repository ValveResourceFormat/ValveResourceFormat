#version 460

#define D_GROUP_SIZE 64

layout(local_size_x = D_GROUP_SIZE, local_size_y = 1, local_size_z = 1) in;

layout(binding = 12, std430) readonly buffer LightCullBitsBuffer
{
    uint g_CullBits[];
};

layout(binding = 2, std430) writeonly buffer VisibleCullBitsBuffer
{
    uint g_VisibleCullBits[];
};

uniform uint g_nReduceTileBase;
uniform uint g_nReduceTileCount;
uniform uint g_nReduceWords;

shared uint g_nLaneBits[D_GROUP_SIZE];

void main()
{
    uint nWord = gl_WorkGroupID.x;
    uint nLane = gl_LocalInvocationID.x;

    uint nBits = 0u;

    for (uint nTile = nLane; nTile < g_nReduceTileCount; nTile += uint(D_GROUP_SIZE))
    {
        nBits |= g_CullBits[g_nReduceTileBase + (nTile * g_nReduceWords) + nWord];
    }

    g_nLaneBits[nLane] = nBits;

    barrier();

    for (uint nStride = uint(D_GROUP_SIZE) / 2u; nStride > 0u; nStride >>= 1u)
    {
        if (nLane < nStride)
        {
            g_nLaneBits[nLane] |= g_nLaneBits[nLane + nStride];
        }

        barrier();
    }

    if (nLane == 0u)
    {
        g_VisibleCullBits[nWord] = g_nLaneBits[0];
    }
}
