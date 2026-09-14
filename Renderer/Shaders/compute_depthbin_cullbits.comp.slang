#version 460

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

#include "common/light_cull_structures.slang"

void main()
{
    uint nBatch = (gl_WorkGroupID.y >= g_nFirstMaskForBatch.z)
                ? 2u
                : uint(gl_WorkGroupID.y >= g_nFirstMaskForBatch.y);
    uint nMaskIndex = gl_WorkGroupID.y - g_nFirstMaskForBatch[nBatch];
    CullBatch_t batch = g_BinBatches[nBatch];

    uint nBin = gl_GlobalInvocationID.x;
    float flBinNear = (g_fNearPlane + (float(nBin) * g_fDepthBinWidth)) - g_fBinEpsilon;
    float flBinFar = (g_fNearPlane + (float(nBin + 1u) * g_fDepthBinWidth)) + g_fBinEpsilon;

    uint nFirstItem = batch.nFirstItem + (nMaskIndex * 32u);
    uint nLastItem = min(batch.nItemEnd, nFirstItem + 32u);

    uint nBits = 0u;

    for (uint i = nFirstItem; i < nLastItem; i++)
    {
        CullItem_t item = g_CullItems[i];

        if (item.flDepthMin <= flBinFar && item.flDepthMax >= flBinNear)
        {
            nBits |= 1u << (i - nFirstItem);
        }
    }

    g_OutputCullBits[batch.nOutputOffset + (nBin * batch.nOutputStride) + nMaskIndex] = nBits;
}
