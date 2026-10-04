using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Exceptions;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace Tests.Formats
{
    public class KeyValuesTest
    {
        [Test]
        public async Task TestKeyValues3_LF()
        {
            var file = KVDocumentExtensions.ParseKV3(TestFixtures.Path("KeyValues", "KeyValues3_LF.kv3"));
            await Assert.That(file.Header!.Encoding.ToString()).IsEqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}");
            await AssertKV3Properties(file);
        }

        [Test]
        public async Task TestBinaryKV3_Serialization()
        {
            var originalFile = KVDocumentExtensions.ParseKV3(TestFixtures.Path("KeyValues", "KeyValues3_LF.kv3"));

            var binaryKV3 = new BinaryKV3(originalFile.Root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 5,
            };

            var deserializedFile = RoundTrip(binaryKV3).Data;
            await AssertKV3Properties(deserializedFile);
        }

        [Test]
        [MatrixDataSource]
        public async Task TestBinaryKV3Serialization(
            [Matrix(4, 5)] int version,
            [Matrix] KV3BinaryCompressionMethod compressionMethod)
        {
            var originalFile = KVDocumentExtensions.ParseKV3(TestFixtures.Path("KeyValues", "KeyValues3_LF.kv3"));
            var smallBlob = new byte[100];
            var largeBlob = new byte[32769];

            for (var i = 0; i < smallBlob.Length; i++)
            {
                smallBlob[i] = (byte)(i % 17);
            }

            for (var i = 0; i < largeBlob.Length; i++)
            {
                largeBlob[i] = (byte)(i % 251);
            }

            originalFile.Root["smallBlob"] = KVObject.Blob(smallBlob);
            originalFile.Root["largeBlob"] = KVObject.Blob(largeBlob);
            originalFile.Root["emptyBlob"] = KVObject.Blob([]);
            originalFile.Root["null"] = KVObject.Null();
            originalFile.Root["int16"] = new KVObject((short)-123);
            originalFile.Root["uint16"] = new KVObject((ushort)456);
            originalFile.Root["int32"] = new KVObject(-789);
            originalFile.Root["uint32"] = new KVObject(987U);
            originalFile.Root["float"] = new KVObject(1.25F);
            originalFile.Root["negativeZero"] = new KVObject(-0.0D);
            originalFile.Root["emptyString"] = new KVObject(string.Empty);
            originalFile.Root["emptyArray"] = KVObject.Array();
            originalFile.Root["emptyObject"] = KVObject.Collection();
            var binaryKV3 = new BinaryKV3(originalFile.Root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = version,
                SerializationCompressionMethod = compressionMethod,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            using (Assert.Multiple())
            {
                await Assert.That(deserializedBinaryKV3.SerializationVersion).IsEqualTo(version);
                await Assert.That(deserializedBinaryKV3.SerializationCompressionMethod).IsEqualTo(compressionMethod);
                await Assert.That(deserializedBinaryKV3.Data.Root["smallBlob"].AsBlob()).IsEquivalentTo(smallBlob, CollectionOrdering.Matching);
                await Assert.That(deserializedBinaryKV3.Data.Root["largeBlob"].AsBlob()).IsEquivalentTo(largeBlob, CollectionOrdering.Matching);
                await Assert.That(deserializedBinaryKV3.Data.Root["emptyBlob"].AsBlob()).IsEmpty();
                await Assert.That((string)deserializedBinaryKV3.Data.Root["stringValue"]).IsEqualTo("hello world");
                await Assert.That(deserializedBinaryKV3.Data.Root["stringThatIsAResourceReference"].Flag).IsEqualTo(KVFlag.Resource);
                await Assert.That(deserializedBinaryKV3.Data.Root["null"].ValueType).IsEqualTo(KVValueType.Null);
                await Assert.That((short)deserializedBinaryKV3.Data.Root["int16"]).IsEqualTo((short)-123);
                await Assert.That((ushort)deserializedBinaryKV3.Data.Root["uint16"]).IsEqualTo((ushort)456);
                await Assert.That((int)deserializedBinaryKV3.Data.Root["int32"]).IsEqualTo(-789);
                await Assert.That((uint)deserializedBinaryKV3.Data.Root["uint32"]).IsEqualTo((uint)987);
                await Assert.That((float)deserializedBinaryKV3.Data.Root["float"]).IsEqualTo(1.25F);
                await Assert.That(BitConverter.DoubleToInt64Bits((double)deserializedBinaryKV3.Data.Root["negativeZero"])).IsEqualTo(long.MinValue);
                await Assert.That((string)deserializedBinaryKV3.Data.Root["emptyString"]).IsEmpty();
                await Assert.That(deserializedBinaryKV3.Data.Root["emptyArray"]).IsEmpty();
                await Assert.That(deserializedBinaryKV3.Data.Root["emptyObject"]).IsEmpty();
                await Assert.That(deserializedBinaryKV3.Data.ToKV3String()).IsEqualTo(originalFile.ToKV3String());
            }
        }

        [Test]
        [MethodDataSource(nameof(BinaryKV3FixtureSerializationCases))]
        public async Task TestBinaryKV3FixtureSerialization(
            string fileName,
            BlockType blockType,
            int expectedBlobCount,
            int expectedBlobBytes,
            int version,
            KV3BinaryCompressionMethod compressionMethod)
        {
            var file = TestFixtures.Path(fileName);
            using var resource = new Resource();
            resource.Read(file);

            var block = resource.Blocks.Single(block => block.Type == blockType);
            using var sourceStream = File.OpenRead(file);
            var binaryKV3 = ReadBinaryKV3Block(sourceStream, block);
            var expectedBlobs = CollectBinaryBlobs(binaryKV3.Data.Root);

            using (Assert.Multiple())
            {
                await Assert.That(expectedBlobs).Count().IsEqualTo(expectedBlobCount);
                await Assert.That(expectedBlobs.Sum(blob => blob.Length)).IsEqualTo(expectedBlobBytes);
            }

            binaryKV3.SerializationVersion = version;
            binaryKV3.SerializationCompressionMethod = compressionMethod;
            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);

            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);
            var actualBlobs = CollectBinaryBlobs(deserializedBinaryKV3.Data.Root);

            using (Assert.Multiple())
            {
                await Assert.That(deserializedBinaryKV3.SerializationVersion).IsEqualTo(version);
                await Assert.That(deserializedBinaryKV3.SerializationCompressionMethod).IsEqualTo(compressionMethod);
                await Assert.That(actualBlobs).Count().IsEqualTo(expectedBlobs.Count);
                await Assert.That(deserializedBinaryKV3.Data.ToKV3String()).IsEqualTo(binaryKV3.Data.ToKV3String());

                for (var i = 0; i < expectedBlobs.Count; i++)
                {
                    await Assert.That(actualBlobs[i]).IsEquivalentTo(expectedBlobs[i], CollectionOrdering.Matching).Because($"Blob {i}");
                }
            }
        }

        [Test]
        [Arguments(KV3BinaryCompressionMethod.Uncompressed)]
        [Arguments(KV3BinaryCompressionMethod.Lz4)]
        [Arguments(KV3BinaryCompressionMethod.Zstd)]
        public async Task TestBinaryKV3Version5EmptyBlob(KV3BinaryCompressionMethod compressionMethod)
        {
            var child = KVObject.Collection();
            child["duplicate"] = "same";
            child["emptyValue"] = string.Empty;
            var root = KVObject.Collection();
            root["emptyBlob"] = KVObject.Blob([]);
            root["duplicate"] = "same";
            root["child"] = child;
            root[string.Empty] = "same";
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 5,
                SerializationCompressionMethod = compressionMethod,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            using (Assert.Multiple())
            {
                await Assert.That(deserializedBinaryKV3.Data.Root["emptyBlob"].AsBlob()).IsEmpty();
                await Assert.That((string)deserializedBinaryKV3.Data.Root["duplicate"]).IsEqualTo("same");
                await Assert.That((string)deserializedBinaryKV3.Data.Root["child"]["emptyValue"]).IsEmpty();
            }
        }

        [Test]
        [MatrixDataSource]
        public async Task TestBinaryKV3NonObjectRoot([Matrix(4, 5)] int version)
        {
            var root = KVObject.Array();
            root.Add(42);
            root.Add("root array");
            root.Add(KVObject.Collection());
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = version,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            await Assert.That(deserializedBinaryKV3.Data.Root.IsArray).IsTrue();
            await Assert.That((int)deserializedBinaryKV3.Data.Root[0]!).IsEqualTo(42);
            await Assert.That((string)deserializedBinaryKV3.Data.Root[1]!).IsEqualTo("root array");
            await Assert.That(deserializedBinaryKV3.Data.Root[2]).IsEmpty();
        }

        [Test]
        public async Task TestBinaryKV3Version5Lz4BlobFramesRespectBlobBoundaries()
        {
            var firstBlob = new byte[] { 1 };
            var secondBlob = new byte[16385];
            var thirdBlob = new byte[200];

            for (var i = 0; i < secondBlob.Length; i++)
            {
                secondBlob[i] = (byte)(i % 251);
            }

            Array.Fill(thirdBlob, (byte)0xA5);
            var root = KVObject.Array([
                KVObject.Blob(firstBlob),
                KVObject.Blob(secondBlob),
                KVObject.Blob(thirdBlob),
            ]);
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 5,
                SerializationCompressionMethod = KV3BinaryCompressionMethod.Lz4,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            using (Assert.Multiple())
            {
                await Assert.That(deserializedBinaryKV3.Data.Root[0]!.AsBlob()).IsEquivalentTo(firstBlob, CollectionOrdering.Matching);
                await Assert.That(deserializedBinaryKV3.Data.Root[1]!.AsBlob()).IsEquivalentTo(secondBlob, CollectionOrdering.Matching);
                await Assert.That(deserializedBinaryKV3.Data.Root[2]!.AsBlob()).IsEquivalentTo(thirdBlob, CollectionOrdering.Matching);
            }
        }

        [Test]
        public async Task TestBinaryKV3SerializationValidation()
        {
            var root = KVObject.Collection();
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic")) { Resource = null! };

            await Assert.That(binaryKV3.SerializationCompressionMethod).IsEqualTo(KV3BinaryCompressionMethod.Uncompressed);

            binaryKV3.SerializationVersion = 99;
            await Assert.That(() => binaryKV3.Serialize(new MemoryStream())).ThrowsExactly<NotSupportedException>();

            binaryKV3.SerializationVersion = 5;
            binaryKV3.SerializationCompressionMethod = (KV3BinaryCompressionMethod)99;
            await Assert.That(() => binaryKV3.Serialize(new MemoryStream())).ThrowsExactly<NotSupportedException>();
        }

        [Test]
        public async Task TestBinaryKV3Version4ZstdBlobsAreSecondFrame()
        {
            var root = KVObject.Collection();
            root["blob"] = KVObject.Blob([1, 2, 3, 4]);
            root["value"] = "hello";
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 4,
                SerializationCompressionMethod = KV3BinaryCompressionMethod.Zstd,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();

            // The first frame must hold only the buffer, readers stop at its end and stream the blobs separately
            var sizeUncompressed = BitConverter.ToInt32(data, HeaderStart + 28);
            var sizeCompressed = BitConverter.ToInt32(data, HeaderStart + 32);
            var frames = data.AsSpan(HeaderStart + 52, sizeCompressed);
            var firstFrameLength = GetZstdFrameLength(frames);
            var firstFrameContentSize = ZstdSharp.Decompressor.GetDecompressedSize(frames[..firstFrameLength]);
            var secondFrameContentSize = ZstdSharp.Decompressor.GetDecompressedSize(frames[firstFrameLength..]);

            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);

            using (Assert.Multiple())
            {
                await Assert.That(firstFrameContentSize).IsEqualTo((ulong)sizeUncompressed);
                await Assert.That(secondFrameContentSize).IsEqualTo(4UL);
                await Assert.That(deserializedBinaryKV3.Data.Root["blob"].AsBlob()).IsEquivalentTo(new byte[] { 1, 2, 3, 4 }, CollectionOrdering.Matching);
                await Assert.That((string)deserializedBinaryKV3.Data.Root["value"]).IsEqualTo("hello");
            }
        }

        [Test]
        public async Task TestBinaryKV3Version5HeaderCounts()
        {
            var root = KVObject.Collection();
            root["array"] = KVObject.Array([1, 2]);
            root["object"] = KVObject.Collection();
            root["emptyArray"] = KVObject.Array();
            root["blob"] = KVObject.Blob([1, 2, 3]);
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 5,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();
            int Header(int offset) => BitConverter.ToInt32(data, HeaderStart + offset);

            using (Assert.Multiple())
            {
                await Assert.That(BitConverter.ToUInt16(data, HeaderStart + 24)).IsEqualTo((ushort)2);
                await Assert.That(BitConverter.ToUInt16(data, HeaderStart + 26)).IsEqualTo((ushort)2);
                await Assert.That(Header(84)).IsEqualTo(7); // root, 4 members, 2 array elements
                await Assert.That(Header(88)).IsEqualTo(2);
                await Assert.That(Header(92)).IsEqualTo(2);
                await Assert.That(Header(96)).IsEqualTo(3); // the empty array counts as one element
                await Assert.That(Header(32)).IsEqualTo(Header(28)); // uncompressed blobs are not part of the compressed size
            }
        }

        [Test]
        public async Task TestBinaryKV3Version5ManyObjects()
        {
            var root = KVObject.Array();

            for (var i = 0; i < 70000; i++)
            {
                root.Add(KVObject.Collection());
            }

            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = 5,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();

            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);

            using (Assert.Multiple())
            {
                await Assert.That(BitConverter.ToUInt16(data, HeaderStart + 24)).IsEqualTo(ushort.MaxValue);
                await Assert.That(BitConverter.ToInt32(data, HeaderStart + 88)).IsEqualTo(70000);
                await Assert.That(deserializedBinaryKV3.Data.Root).Count().IsEqualTo(70000);
            }
        }

        [Test]
        public async Task TestBinaryKV3ReadsInt8AndUInt8()
        {
            var binaryKV3 = ReadCraftedBinaryKV3(4, bytes1: [0xFF, 0xFF], bytes4: [2, 0, 1], strings: ["a", "b"], types: [9, 22, 23]);

            using (Assert.Multiple())
            {
                await Assert.That(binaryKV3.Data.Root["a"].ValueType).IsEqualTo(KVValueType.Int32);
                await Assert.That((int)binaryKV3.Data.Root["a"]).IsEqualTo(-1);
                await Assert.That(binaryKV3.Data.Root["b"].ValueType).IsEqualTo(KVValueType.UInt32);
                await Assert.That((uint)binaryKV3.Data.Root["b"]).IsEqualTo(255U);
            }
        }

        [Test]
        public async Task TestBinaryKV3SkipsByteAfterTypeWithBit6()
        {
            var binaryKV3 = ReadCraftedBinaryKV3(4, bytes1: [], bytes4: [2, 0, 42, 1, 43], strings: ["a", "b"], types: [9, 0x40 | 11, 0xAB, 11]);

            using (Assert.Multiple())
            {
                await Assert.That((int)binaryKV3.Data.Root["a"]).IsEqualTo(42);
                await Assert.That((int)binaryKV3.Data.Root["b"]).IsEqualTo(43);
            }
        }

        [Test]
        public async Task TestBinaryKV3InvalidStringIdsAreEmpty()
        {
            var binaryKV3 = ReadCraftedBinaryKV3(4, bytes1: [], bytes4: [2, 0, 7, -5, 0], strings: ["a"], types: [9, 6, 6]);

            using (Assert.Multiple())
            {
                await Assert.That((string)binaryKV3.Data.Root["a"]).IsEmpty();
                await Assert.That((string)binaryKV3.Data.Root[string.Empty]).IsEqualTo("a");
            }
        }

        [Test]
        public async Task TestBinaryKV3RepeatedMemberNameReplacesValue()
        {
            var binaryKV3 = ReadCraftedBinaryKV3(4, bytes1: [], bytes4: [2, 0, 1, 0, 2], strings: ["a"], types: [9, 11, 11]);

            using (Assert.Multiple())
            {
                await Assert.That(binaryKV3.Data.Root).Count().IsEqualTo(1);
                await Assert.That((int)binaryKV3.Data.Root["a"]).IsEqualTo(2);
            }
        }

        [Test]
        public async Task TestBinaryKV3RejectsEmptyTypedArray()
        {
            await Assert.That(() => ReadCraftedBinaryKV3(4, bytes1: [0], bytes4: [], strings: [], types: [24, 11])).Throws<InvalidDataException>();
        }

        [Test]
        public async Task TestBinaryKV3Version2FlagBits()
        {
            // Resource, ResourceName and Panorama at once, a lone multiline bit, and multiline with SoundEvent
            var binaryKV3 = ReadCraftedBinaryKV3(2, bytes1: [], bytes4: [3, 0, 0, 1, 1, 2, 2], strings: ["a", "b", "c"], types: [9, 0x86, 0x0B, 0x86, 0x04, 0x86, 0x14]);

            using (Assert.Multiple())
            {
                await Assert.That(binaryKV3.Data.Root["a"].Flag).IsEqualTo(KVFlag.Resource);
                await Assert.That(binaryKV3.Data.Root["b"].Flag).IsEqualTo(KVFlag.None);
                await Assert.That(binaryKV3.Data.Root["c"].Flag).IsEqualTo(KVFlag.SoundEvent);
            }
        }

        [Test]
        public async Task TestBinaryKV3RejectsEmptyFlag()
        {
            await Assert.That(() => ReadCraftedBinaryKV3(4, bytes1: [], bytes4: [1, 0, 0], strings: ["a"], types: [9, 0x86, 0x00])).Throws<UnexpectedMagicException>();
        }

        private static int GetZstdFrameLength(ReadOnlySpan<byte> data)
        {
            var descriptor = data[4];
            var contentSizeFlag = descriptor >> 6;
            var singleSegment = (descriptor & 0x20) != 0;
            var hasChecksum = (descriptor & 0x04) != 0;
            var offset = 5;

            offset += singleSegment ? 0 : 1;
            offset += (descriptor & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
            offset += contentSizeFlag switch { 0 => singleSegment ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };

            while (true)
            {
                var header = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                var blockType = (header >> 1) & 3;
                offset += 3 + (blockType == 1 ? 1 : header >> 3);

                if ((header & 1) != 0)
                {
                    break;
                }
            }

            return offset + (hasChecksum ? 4 : 0);
        }

        // Header fields start after the magic and the format guid
        private const int HeaderStart = 20;

        // Builds an uncompressed version 2 to 4 block without blobs
        private static BinaryKV3 ReadCraftedBinaryKV3(int version, byte[] bytes1, int[] bytes4, string[] strings, byte[] types)
        {
            using var buffer = new MemoryStream();
            int countTypes;

            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(bytes1);

                while (buffer.Length % 4 != 0)
                {
                    writer.Write((byte)0);
                }

                writer.Write(strings.Length);

                foreach (var value in bytes4)
                {
                    writer.Write(value);
                }

                while (buffer.Length % 8 != 0)
                {
                    writer.Write((byte)0);
                }

                var stringsStart = buffer.Length;

                foreach (var value in strings)
                {
                    writer.Write(Encoding.UTF8.GetBytes(value));
                    writer.Write((byte)0);
                }

                writer.Write(types);
                countTypes = (int)(buffer.Length - stringsStart);
                writer.Write(0xFFEEDD00);
            }

            var stream = new MemoryStream();

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(0x4B563300 | version);
                writer.Write(KV3IDLookup.Get("generic").Id.ToByteArray());
                writer.Write(0); // uncompressed
                writer.Write(0); // dictionary id and frame size
                writer.Write(bytes1.Length);
                writer.Write(bytes4.Length + 1);
                writer.Write(0);
                writer.Write(countTypes);
                writer.Write(0); // object and array counts
                writer.Write((int)buffer.Length);
                writer.Write((int)buffer.Length);
                writer.Write(0);
                writer.Write(0);

                if (version >= 4)
                {
                    writer.Write(0);
                    writer.Write(0);
                }

                writer.Write(buffer.ToArray());
            }

            stream.Position = 0;
            return ReadBinaryKV3(stream);
        }

        [Test]
        [Arguments("ar_dizzy_kv3_v3_uncompressed.vpost_c", BlockType.DATA, KV3BinaryCompressionMethod.Uncompressed)]
        [Arguments("aw_ti9_gargoyle_collision_kv3_v3_zstd.vmdl_c", BlockType.ANIM, KV3BinaryCompressionMethod.Zstd)]
        [Arguments("compute_reactive_mask_kv3_v3_lz4.vmat_c", BlockType.DATA, KV3BinaryCompressionMethod.Lz4)]
        [Arguments("panorama_world_panel_default_kv3_v3_lz4.vmat_c", BlockType.DATA, KV3BinaryCompressionMethod.Lz4)]
        [Arguments("piece_kv3_v4.vmdl_c", BlockType.PHYS, KV3BinaryCompressionMethod.Lz4)]
        [Arguments("default_ents_kv3_v4_zstd.vents_c", BlockType.DATA, KV3BinaryCompressionMethod.Zstd)]
        public async Task TestBinaryKV3SourceCompressionIsPreserved(
            string fileName,
            BlockType blockType,
            KV3BinaryCompressionMethod expectedCompressionMethod)
        {
            var file = TestFixtures.Path(fileName);
            using var resource = new Resource();
            resource.Read(file);

            var block = resource.Blocks.Single(block => block.Type == blockType);
            using var sourceStream = File.OpenRead(file);
            var binaryKV3 = ReadBinaryKV3Block(sourceStream, block);
            await Assert.That(binaryKV3.SerializationCompressionMethod).IsEqualTo(expectedCompressionMethod);

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);
            await Assert.That(deserializedBinaryKV3.SerializationCompressionMethod).IsEqualTo(expectedCompressionMethod);
        }

        [Test]
        public async Task TestBinaryKV3Version5ZstdSourceSettingsArePreserved()
        {
            var file = TestFixtures.Path("abilities_kv3_v5_zstd.vdata_c");
            using var resource = new Resource();
            resource.Read(file);

            var blocks = resource.Blocks.OfType<BinaryKV3>().Where(block => block.SerializationVersion == 5).ToList();
            await Assert.That(blocks).IsNotEmpty();

            foreach (var binaryKV3 in blocks)
            {
                await Assert.That(binaryKV3.SerializationCompressionMethod).IsEqualTo(KV3BinaryCompressionMethod.Zstd);
                var reparsed = RoundTrip(binaryKV3);
                await Assert.That(reparsed.SerializationCompressionMethod).IsEqualTo(KV3BinaryCompressionMethod.Zstd);
                await Assert.That(reparsed.SerializationVersion).IsEqualTo(5);
            }
        }

        [Test]
        public async Task TestDeadlockBinaryKV3Version5SourceSettingsArePreserved()
        {
            var file = TestFixtures.Path("deadlock_tracked_stats_player_staging_kv3_v5.vdata_c");
            using var resource = new Resource();
            resource.Read(file);
            var expectedCompressions = new Dictionary<BlockType, KV3BinaryCompressionMethod>
            {
                [BlockType.RED2] = KV3BinaryCompressionMethod.Lz4,
                [BlockType.DATA] = KV3BinaryCompressionMethod.Uncompressed,
                [BlockType.FLCI] = KV3BinaryCompressionMethod.Uncompressed,
            };
            var expectedText = new Dictionary<BlockType, string>();

            foreach (var block in resource.Blocks)
            {
                if (!expectedCompressions.TryGetValue(block.Type, out var expectedCompression))
                {
                    continue;
                }

                var data = block switch
                {
                    BinaryKV3 binaryKV3 => binaryKV3.Data,
                    ValveResourceFormat.Blocks.ResourceEditInfo2 resourceEditInfo => resourceEditInfo.Data!,
                    _ => throw new AssertionException($"Expected {block.Type} to contain binary KV3 data."),
                };
                expectedText[block.Type] = data.ToKV3String();

                if (block is BinaryKV3 sourceBinaryKV3)
                {
                    await Assert.That(sourceBinaryKV3.SerializationVersion).IsEqualTo(5).Because(block.Type.ToString());
                    await Assert.That(sourceBinaryKV3.SerializationCompressionMethod).IsEqualTo(expectedCompression).Because(block.Type.ToString());
                }
            }

            using var stream = new MemoryStream();
            resource.Serialize(stream);
            stream.Position = 0;
            using var reparsedResource = new Resource();
            reparsedResource.Read(stream);
            var found = 0;

            foreach (var block in reparsedResource.Blocks)
            {
                if (!expectedCompressions.TryGetValue(block.Type, out var expectedCompression))
                {
                    continue;
                }

                found++;
                var binaryKV3 = block as BinaryKV3 ?? ReadBinaryKV3Block(stream, block);

                using (Assert.Multiple())
                {
                    await Assert.That(binaryKV3.SerializationVersion).IsEqualTo(5).Because(block.Type.ToString());
                    await Assert.That(binaryKV3.SerializationCompressionMethod).IsEqualTo(expectedCompression).Because(block.Type.ToString());
                    await Assert.That(binaryKV3.Data.ToKV3String()).IsEqualTo(expectedText[block.Type]).Because(block.Type.ToString());
                }
            }

            await Assert.That(found).IsEqualTo(expectedCompressions.Count);
        }

        private static BinaryKV3 ReadBinaryKV3Block(Stream stream, Block block)
        {
            var binaryKV3 = new BinaryKV3(block.Type)
            {
                Size = block.Size,
                Offset = block.Offset,
                Resource = block.Resource,
            };

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            binaryKV3.Read(reader);
            return binaryKV3;
        }

        private static BinaryKV3 RoundTrip(BinaryKV3 binaryKV3)
        {
            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            stream.Position = 0;
            return ReadBinaryKV3(stream);
        }

        private static BinaryKV3 ReadBinaryKV3(Stream stream)
        {
            var deserializedBinaryKV3 = new BinaryKV3(BlockType.DATA)
            {
                Size = (uint)stream.Length,
                Offset = 0,
                Resource = null!,
            };

            using var reader = new BinaryReader(stream);
            deserializedBinaryKV3.Read(reader);
            return deserializedBinaryKV3;
        }

        public static IEnumerable<(string, BlockType, int, int, int, KV3BinaryCompressionMethod)> BinaryKV3FixtureSerializationCases()
        {
            var fixtures = new[]
            {
                ("basepostprocess_kv3_v4_uncompressed.vpost_c", BlockType.DATA, 1, 131072),
                ("piece_kv3_v4.vmdl_c", BlockType.PHYS, 7, 1378),
            };

            foreach (var (fileName, blockType, blobCount, blobBytes) in fixtures)
            {
                foreach (var version in new[] { 4, 5 })
                {
                    foreach (var compressionMethod in Enum.GetValues<KV3BinaryCompressionMethod>())
                    {
                        yield return (fileName, blockType, blobCount, blobBytes, version, compressionMethod);
                    }
                }
            }
        }

        private static List<byte[]> CollectBinaryBlobs(KVObject value)
        {
            List<byte[]> blobs = [];
            CollectBinaryBlobs(value, blobs);
            return blobs;
        }

        private static void CollectBinaryBlobs(KVObject value, List<byte[]> blobs)
        {
            if (value.ValueType == KVValueType.BinaryBlob)
            {
                blobs.Add(value.AsBlob());
                return;
            }

            if (value.ValueType is not (KVValueType.Collection or KVValueType.Array))
            {
                return;
            }

            foreach (var (_, child) in value)
            {
                CollectBinaryBlobs(child, blobs);
            }
        }

        private static async Task AssertKV3Properties(KVDocument file)
        {
            using (Assert.Multiple())
            {
                //Not sure what KVType is better for this
                await Assert.That((string)file.Root["multiLineStringValue"]).IsEqualTo("First line of a multi-line string literal.\nSecond line of a multi-line string literal.");

                await Assert.That(file.Header!.Format.ToString()).IsEqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}");

                await Assert.That(file.Root).Count().IsEqualTo(14);

                await Assert.That(file.Root["boolValue"].ValueType).IsEqualTo(KVValueType.Boolean);
                await Assert.That((bool)file.Root["boolValue"]).IsFalse();
                await Assert.That(file.Root["intValue"].ValueType).IsEqualTo(KVValueType.UInt64);
                await Assert.That((ulong)file.Root["intValue"]).IsEqualTo((ulong)128);
                await Assert.That(file.Root["doubleValue"].ValueType).IsEqualTo(KVValueType.FloatingPoint64);
                await Assert.That((double)file.Root["doubleValue"]).IsEqualTo(64.000000);
                await Assert.That(file.Root["negativeIntValue"].ValueType).IsEqualTo(KVValueType.Int64);
                await Assert.That((long)file.Root["negativeIntValue"]).IsEqualTo((long)-1337);
                await Assert.That(file.Root["negativeDoubleValue"].ValueType).IsEqualTo(KVValueType.FloatingPoint64);
                await Assert.That((double)file.Root["negativeDoubleValue"]).IsEqualTo(-0.133700);
                await Assert.That(file.Root["stringValue"].ValueType).IsEqualTo(KVValueType.String);
                await Assert.That((string)file.Root["stringValue"]).IsEqualTo("hello world");

                //Do special test for flagged value
                var flagValue = file.Root["stringThatIsAResourceReference"];
                await Assert.That((string)flagValue).IsEqualTo("particles/items3_fx/star_emblem.vpcf");
                await Assert.That(flagValue.Flag).IsEqualTo(KVFlag.Resource);

                await Assert.That(file.Root["arrayValue"].ValueType).IsEqualTo(KVValueType.Array);
                var arrayValue = file.Root["arrayValue"];
                Debug.Assert(arrayValue != null);
                await Assert.That((ulong)arrayValue[0]!).IsEqualTo((ulong)1);
                await Assert.That((ulong)arrayValue[1]!).IsEqualTo((ulong)2);
                await Assert.That((string)arrayValue[2]!).IsEqualTo("characters/models/shared/animsets/animset_ct.vmdl");
                await Assert.That(arrayValue[2]!.Flag).IsEqualTo(KVFlag.Resource);
                await Assert.That((string)arrayValue[3]!).IsEqualTo("hud/abilities/haze/haze_sleep_dagger.psd");
                await Assert.That(arrayValue[3]!.Flag).IsEqualTo(KVFlag.Panorama);
                await Assert.That((string)arrayValue[4]!).IsEqualTo("hello world");
                await Assert.That(arrayValue[5]!.Flag).IsEqualTo(KVFlag.SoundEvent);
                await Assert.That(arrayValue[6]!.Flag).IsEqualTo(KVFlag.SubClass);
                await Assert.That(arrayValue[7]!.Flag).IsEqualTo(KVFlag.EntityName);

                await Assert.That(file.Root["objectValue"].ValueType).IsEqualTo(KVValueType.Collection);
                var objectValue = file.Root["objectValue"];
                Debug.Assert(objectValue != null);
                await Assert.That((ulong)objectValue["n"]).IsEqualTo((ulong)5);
                await Assert.That((string)objectValue["s"]).IsEqualTo("foo");

                var binaryBlobValue = file.Root["binaryBlobValue"];
                await Assert.That(binaryBlobValue.ValueType).IsEqualTo(KVValueType.BinaryBlob);
                await Assert.That(binaryBlobValue.AsBlob()).Count().IsEqualTo(40);
                await Assert.That(Encoding.UTF8.GetString(binaryBlobValue.AsBlob())).IsEqualTo("Hello, this is a test binary blob value!");

                await Assert.That(file.Root["arrayOnSingleLine"].ValueType).IsEqualTo(KVValueType.Array);

                await Assert.That((string)file.Root["quoted.key"]).IsEqualTo("hello");
                await Assert.That((string)file.Root["a quoted key with spaces"]).IsEqualTo("some cool value");
            }
        }

        [Test]
        public async Task TestKV3Guids()
        {
            using (Assert.Multiple())
            {
                foreach (var (name, guid) in KV3IDLookup.Table)
                {
                    if (name == "vpcf38") // Classic valve
                    {
                        await Assert.That(guid.Version).IsEqualTo(1).Because(name);
                        continue;
                    }

                    await Assert.That(guid.Version).IsEqualTo(4).Because(name);
                }
            }
        }

        [Test]
        public async Task TestKV3StringEscaping()
        {
            var expectedFilePath = TestFixtures.Path("KeyValues", "StringEscaping.kv3");

            var parsedFile = KVDocumentExtensions.ParseKV3(expectedFilePath);
            var serializedOutput = parsedFile.ToKV3String().Trim().ReplaceLineEndings();
            var expectedOutput = (await File.ReadAllTextAsync(expectedFilePath)).Trim().ReplaceLineEndings();

            await Assert.That(serializedOutput).IsEqualTo(expectedOutput);
        }
    }
}
