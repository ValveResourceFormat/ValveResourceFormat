using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.Compression;

// Copied from https://github.com/zeux/meshoptimizer/blob/master/demo/tests.cpp
// and https://github.com/zeux/meshoptimizer/blob/master/js/meshopt_decoder.test.js

namespace Tests
{
    public partial class MeshOptimizerTest
    {
        private static readonly byte[] kIndexMalformedVByte = [
            0xe1, 0x20, 0x20, 0x20, 0xff, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
            0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
            0xff, 0xff, 0xff, 0xff, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
            0x20, 0x20, 0x20,
        ];

        // header followed by the 16-byte codeaux table
        private static readonly byte[] kIndexEmptyData = [
            0xe1, 0x00, 0x76, 0x87, 0x56, 0x67, 0x78, 0xa9, 0x86, 0x65, 0x89, 0x68, 0x98, 0x01, 0x69, 0x00, 0x00,
        ];

        // header followed by a 24-byte padded tail for a 16-byte vertex
        private static readonly byte[] kVertexEmptyData = [
            0xa1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        private static readonly byte[] kVertexSentinelCountExpected = [
            0xff, 0, 0, 0, 0xfe, 0, 0, 0, 0xfd, 0, 0, 0, 0xfd, 0, 0, 0,
            0xfd, 0, 0, 0, 0xfc, 0, 0, 0, 0xfb, 0, 0, 0, 0xfb, 0, 0, 0,
            0xfa, 0, 0, 0, 0xfa, 0, 0, 0, 0xf9, 0, 0, 0, 0xf9, 0, 0, 0,
            0xf8, 0, 0, 0,
        ];

        // encodes several 2-bit sentinels including lane 12; lane 3 is clear so bit 30 does not alias bit 0 during counting
        private static readonly byte[] kVertexSentinelCountData = [
            0xa1, 0xa9,
            0x01, 0xfc, 0x3c, 0xcc, 0xcc,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

        private static readonly byte[] kVertexBitXorRotateData = [
            0xa1, 0xab, 0xab, 0xfa, 0xff, 0x00, 0x02, 0x02, 0x02, 0x00, 0xa5, 0xc2, 0x3a, 0x00, 0xab, 0xf9, 0x57, 0x00, 0x95, 0x42, 0x85, 0x00, 0xdd,
            0xca, 0x6d, 0x00, 0xac, 0x1a, 0x50, 0x00, 0x5c, 0x99, 0xfe, 0x00, 0x4d, 0x0c, 0x39, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x70,
            0x00, 0x00, 0x00, 0x00, 0x80, 0x07, 0x0c, 0x22, 0x26, 0x3b, 0x86, 0x00, 0x00, 0x12, 0x00,
        ];

        private static readonly uint[] kVertexBitXorRotateExpected = [
            0, 112, 201818112, 2252023330, 1, 29, 1188167680, 1600748723, 2, 126, 1739489280, 1696368920, 3, 155, 621084672, 1218163169,
        ];

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndex16()
        {
            var decoded = MeshOptimizerIndexDecoder.DecodeIndexBuffer(kIndexBuffer.Length, sizeof(ushort), kIndexDataV0);
            var expected = Array.ConvertAll(kIndexBuffer, i => (ushort)i);
            await Assert.That(MemoryMarshal.Cast<byte, ushort>(decoded).ToArray()).IsEquivalentTo(expected, CollectionOrdering.Matching);
        }

        [Test]
        [Category("Index Decoder")]
        [Arguments(0)]
        [Arguments(1)]
        public async Task DecodeIndexMemorySafe(int version)
        {
            var buffer = version == 0 ? kIndexDataV0 : kIndexDataV1;
            var indexCount = version == 0 ? kIndexBuffer.Length : kIndexBufferTricky.Length;

            for (var i = 0; i < buffer.Length; i++)
            {
                var shortBuffer = buffer[..i];
                await Assert.That(() => MeshOptimizerIndexDecoder.DecodeIndexBuffer(indexCount, sizeof(int), shortBuffer)).ThrowsException();
            }
        }

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndexRejectExtraBytes()
        {
            byte[] largeBuffer = [.. kIndexDataV0, 0];
            await Assert.That(() => MeshOptimizerIndexDecoder.DecodeIndexBuffer(kIndexBuffer.Length, sizeof(int), largeBuffer)).Throws<InvalidDataException>();
        }

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndexRejectMalformedHeaders()
        {
            byte[] brokenBuffer = [.. kIndexDataV0];
            brokenBuffer[0] = 0;
            await Assert.That(() => MeshOptimizerIndexDecoder.DecodeIndexBuffer(kIndexBuffer.Length, sizeof(int), brokenBuffer)).Throws<ArgumentException>();
        }

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndexRejectInvalidVersion()
        {
            byte[] brokenBuffer = [.. kIndexDataV0];
            brokenBuffer[0] |= 0x0f;
            await Assert.That(() => MeshOptimizerIndexDecoder.DecodeIndexBuffer(kIndexBuffer.Length, sizeof(int), brokenBuffer)).Throws<ArgumentException>();
        }

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndexMalformedVByte()
        {
            await Assert.That(() => MeshOptimizerIndexDecoder.DecodeIndexBuffer(66, sizeof(int), kIndexMalformedVByte)).Throws<InvalidDataException>();
        }

        [Test]
        [Category("Index Decoder")]
        public async Task DecodeIndexEmpty()
        {
            var decoded = MeshOptimizerIndexDecoder.DecodeIndexBuffer(0, sizeof(int), kIndexEmptyData);
            await Assert.That(decoded).IsEmpty();
        }

        [Test]
        [Category("Vertex Decoder")]
        [Arguments(0, false)]
        [Arguments(0, true)]
        [Arguments(1, false)]
        [Arguments(1, true)]
        public async Task DecodeVertexMemorySafe(int version, bool useSimd)
        {
            var buffer = version == 0 ? kVertexDataV0 : kVertexDataV1;

            for (var i = 0; i < buffer.Length; i++)
            {
                var shortBuffer = buffer[..i];
                await Assert.That(() => MeshOptimizerVertexDecoder.DecodeVertexBuffer(kVertexBuffer.Length, Marshal.SizeOf<PV>(), shortBuffer, useSimd)).ThrowsException();
            }
        }

        [Test]
        [Category("Vertex Decoder")]
        [Arguments(false)]
        [Arguments(true)]
        public async Task DecodeVertexRejectExtraBytes(bool useSimd)
        {
            byte[] largeBuffer = [.. kVertexDataV1, 0];
            await Assert.That(() => MeshOptimizerVertexDecoder.DecodeVertexBuffer(kVertexBuffer.Length, Marshal.SizeOf<PV>(), largeBuffer, useSimd)).Throws<ArgumentException>();
        }

        [Test]
        [Category("Vertex Decoder")]
        public async Task DecodeVertexRejectMalformedHeaders()
        {
            byte[] brokenBuffer = [.. kVertexDataV1];
            brokenBuffer[0] = 0;
            await Assert.That(() => MeshOptimizerVertexDecoder.DecodeVertexBuffer(kVertexBuffer.Length, Marshal.SizeOf<PV>(), brokenBuffer)).Throws<ArgumentException>();
        }

        [Test]
        [Category("Vertex Decoder")]
        public async Task DecodeVertexRejectInvalidVersion()
        {
            byte[] brokenBuffer = [.. kVertexDataV1];
            brokenBuffer[0] |= 0x0f;
            await Assert.That(() => MeshOptimizerVertexDecoder.DecodeVertexBuffer(kVertexBuffer.Length, Marshal.SizeOf<PV>(), brokenBuffer)).Throws<ArgumentException>();
        }

        [Test]
        [Category("Vertex Decoder")]
        [Arguments(false)]
        [Arguments(true)]
        public async Task DecodeVertexBitGroupSentinelCount(bool useSimd)
        {
            var decoded = MeshOptimizerVertexDecoder.DecodeVertexBuffer(13, 4, kVertexSentinelCountData, useSimd);
            await Assert.That(decoded).IsEquivalentTo(kVertexSentinelCountExpected, CollectionOrdering.Matching);
        }

        [Test]
        [Category("Vertex Decoder")]
        [Arguments(false)]
        [Arguments(true)]
        public async Task DecodeVertexV1BitXorRotate(bool useSimd)
        {
            var decoded = MeshOptimizerVertexDecoder.DecodeVertexBuffer(4, 16, kVertexBitXorRotateData, useSimd);
            await Assert.That(MemoryMarshal.Cast<byte, uint>(decoded).ToArray()).IsEquivalentTo(kVertexBitXorRotateExpected, CollectionOrdering.Matching);
        }

        [Test]
        [Category("Vertex Decoder")]
        [Arguments(false)]
        [Arguments(true)]
        public async Task DecodeVertexEmpty(bool useSimd)
        {
            var decoded = MeshOptimizerVertexDecoder.DecodeVertexBuffer(0, 16, kVertexEmptyData, useSimd);
            await Assert.That(decoded).IsEmpty();
        }
    }
}
