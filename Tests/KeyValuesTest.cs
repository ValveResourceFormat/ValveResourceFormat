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

namespace Tests
{
    public class KeyValuesTest
    {
        [Test]
        public async Task TestKeyValues3_LF()
        {
            var file = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.TestDirectory!, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            await Assert.That(file.Header!.Encoding.ToString()).IsEqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}");
            await AssertKV3Properties(file);
        }

        [Test]
        public async Task TestBinaryKV3_Serialization()
        {
            var originalFile = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.TestDirectory!, "Files", "KeyValues", "KeyValues3_LF.kv3"));

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
            var originalFile = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.TestDirectory!, "Files", "KeyValues", "KeyValues3_LF.kv3"));
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
            var file = Path.Combine(TestContext.TestDirectory!, "Files", fileName);
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
            var file = Path.Combine(TestContext.TestDirectory!, "Files", fileName);
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
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "abilities_kv3_v5_zstd.vdata_c");
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
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "deadlock_tracked_stats_player_staging_kv3_v5.vdata_c");
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
            var expectedFilePath = Path.Combine(TestContext.TestDirectory!, "Files", "KeyValues", "StringEscaping.kv3");

            var parsedFile = KVDocumentExtensions.ParseKV3(expectedFilePath);
            var serializedOutput = parsedFile.ToKV3String().Trim().ReplaceLineEndings();
            var expectedOutput = (await File.ReadAllTextAsync(expectedFilePath)).Trim().ReplaceLineEndings();

            await Assert.That(serializedOutput).IsEqualTo(expectedOutput);
        }

    }
}
