/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp
 */
using System;

namespace ValveResourceFormat.ThirdParty
{
    public class MeshOptimizerIndexDecoder
    {
        public static byte[] DecodeIndexBuffer(int indexCount, int indexSize, byte[] buffer)
        {
            return new byte[indexCount * indexSize];
        }
    }
}
