using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    [TestFixture]
    public class KeyValuesTest
    {
        [Test]
        public void TestKeyValues3_LF()
        {
            var file = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            Assert.That(file.Header!.Encoding.ToString(), Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
            AssertKV3Properties(file);
        }

        [Test]
        public void TestBinaryKV3_Serialization()
        {
            var originalFile = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));

            var binaryKV3 = new BinaryKV3(originalFile.Root, KV3IDLookup.Get("generic"))
            {
                Resource = null!
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            stream.Position = 0;

            var deserializedBinaryKV3 = new BinaryKV3(BlockType.DATA)
            {
                Size = (uint)stream.Length,
                Offset = 0,
                Resource = null!
            };

            using var reader = new BinaryReader(stream);
            deserializedBinaryKV3.Read(reader);

            var deserializedFile = deserializedBinaryKV3.Data;
            AssertKV3Properties(deserializedFile);
        }

        [TestCase(KV3BinaryCompressionMethod.Uncompressed)]
        [TestCase(KV3BinaryCompressionMethod.Lz4)]
        [TestCase(KV3BinaryCompressionMethod.Zstd)]
        public void TestBinaryKV3Version5Serialization(KV3BinaryCompressionMethod compressionMethod)
        {
            var originalFile = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            var largeBlob = new byte[32769];

            for (var i = 0; i < largeBlob.Length; i++)
            {
                largeBlob[i] = (byte)(i % 251);
            }

            originalFile.Root["largeBlob"] = KVObject.Blob(largeBlob);
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
                SerializationVersion = KV3BinaryVersion.Version5,
                SerializationCompressionMethod = compressionMethod,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(deserializedBinaryKV3.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version5));
                Assert.That(deserializedBinaryKV3.SerializationCompressionMethod, Is.EqualTo(compressionMethod));
                Assert.That(deserializedBinaryKV3.Data.Root["largeBlob"].AsBlob(), Is.EqualTo(largeBlob));
                Assert.That((string)deserializedBinaryKV3.Data.Root["stringValue"], Is.EqualTo("hello world"));
                Assert.That(deserializedBinaryKV3.Data.Root["stringThatIsAResourceReference"].Flag, Is.EqualTo(KVFlag.Resource));
                Assert.That(deserializedBinaryKV3.Data.Root["null"].ValueType, Is.EqualTo(KVValueType.Null));
                Assert.That((short)deserializedBinaryKV3.Data.Root["int16"], Is.EqualTo(-123));
                Assert.That((ushort)deserializedBinaryKV3.Data.Root["uint16"], Is.EqualTo(456));
                Assert.That((int)deserializedBinaryKV3.Data.Root["int32"], Is.EqualTo(-789));
                Assert.That((uint)deserializedBinaryKV3.Data.Root["uint32"], Is.EqualTo(987));
                Assert.That((float)deserializedBinaryKV3.Data.Root["float"], Is.EqualTo(1.25F));
                Assert.That(BitConverter.DoubleToInt64Bits((double)deserializedBinaryKV3.Data.Root["negativeZero"]), Is.EqualTo(long.MinValue));
                Assert.That((string)deserializedBinaryKV3.Data.Root["emptyString"], Is.Empty);
                Assert.That(deserializedBinaryKV3.Data.Root["emptyArray"], Is.Empty);
                Assert.That(deserializedBinaryKV3.Data.Root["emptyObject"], Is.Empty);
            }
        }

        [TestCase(KV3BinaryCompressionMethod.Uncompressed)]
        [TestCase(KV3BinaryCompressionMethod.Lz4)]
        [TestCase(KV3BinaryCompressionMethod.Zstd)]
        public void TestBinaryKV3Version4Serialization(KV3BinaryCompressionMethod compressionMethod)
        {
            var originalFile = KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            var firstBlob = new byte[100];
            var secondBlob = new byte[32769];

            for (var i = 0; i < firstBlob.Length; i++)
            {
                firstBlob[i] = (byte)(i % 17);
            }

            for (var i = 0; i < secondBlob.Length; i++)
            {
                secondBlob[i] = (byte)(i % 251);
            }

            originalFile.Root["firstBlob"] = KVObject.Blob(firstBlob);
            originalFile.Root["secondBlob"] = KVObject.Blob(secondBlob);
            originalFile.Root["emptyBlob"] = KVObject.Blob([]);
            var binaryKV3 = new BinaryKV3(originalFile.Root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = KV3BinaryVersion.Version4,
                SerializationCompressionMethod = compressionMethod,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();
            Assert.That(BitConverter.ToUInt32(data, 20), Is.EqualTo((uint)compressionMethod));

            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(deserializedBinaryKV3.Data.Root["firstBlob"].AsBlob(), Is.EqualTo(firstBlob));
                Assert.That(deserializedBinaryKV3.Data.Root["secondBlob"].AsBlob(), Is.EqualTo(secondBlob));
                Assert.That(deserializedBinaryKV3.Data.Root["emptyBlob"].AsBlob(), Is.Empty);
                Assert.That(deserializedBinaryKV3.Data.ToKV3String(), Is.EqualTo(originalFile.ToKV3String()));
            }
        }

        [TestCaseSource(nameof(BinaryKV3Version4FixtureCases))]
        public void TestBinaryKV3Version4FixtureSerialization(
            string fileName,
            BlockType blockType,
            int expectedBlobCount,
            int expectedBlobBytes,
            KV3BinaryCompressionMethod compressionMethod)
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", fileName);
            using var resource = new Resource();
            resource.Read(file);

            var block = resource.Blocks.Single(block => block.Type == blockType);
            using var sourceStream = File.OpenRead(file);
            var binaryKV3 = ReadBinaryKV3Block(sourceStream, block);
            var expectedBlobs = CollectBinaryBlobs(binaryKV3.Data.Root);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(expectedBlobs, Has.Count.EqualTo(expectedBlobCount));
                Assert.That(expectedBlobs.Sum(blob => blob.Length), Is.EqualTo(expectedBlobBytes));
            }

            binaryKV3.SerializationVersion = KV3BinaryVersion.Version4;
            binaryKV3.SerializationCompressionMethod = compressionMethod;
            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();
            Assert.That(BitConverter.ToUInt32(data, 20), Is.EqualTo((uint)compressionMethod));

            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);
            var actualBlobs = CollectBinaryBlobs(deserializedBinaryKV3.Data.Root);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(actualBlobs, Has.Count.EqualTo(expectedBlobs.Count));
                Assert.That(deserializedBinaryKV3.Data.ToKV3String(), Is.EqualTo(binaryKV3.Data.ToKV3String()));

                for (var i = 0; i < expectedBlobs.Count; i++)
                {
                    Assert.That(actualBlobs[i], Is.EqualTo(expectedBlobs[i]), $"Blob {i}");
                }
            }
        }

        [TestCase(KV3BinaryCompressionMethod.Uncompressed)]
        [TestCase(KV3BinaryCompressionMethod.Lz4)]
        [TestCase(KV3BinaryCompressionMethod.Zstd)]
        public void TestBinaryKV3Version5EmptyBlob(KV3BinaryCompressionMethod compressionMethod)
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
                SerializationVersion = KV3BinaryVersion.Version5,
                SerializationCompressionMethod = compressionMethod,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();
            stream.Position = 0;
            var deserializedBinaryKV3 = ReadBinaryKV3(stream);

            using (Assert.EnterMultipleScope())
            {
                // The table contains: "emptyBlob", "duplicate", "same", "child", and "emptyValue".
                Assert.That(BitConverter.ToInt32(data, 104), Is.EqualTo(5));
                Assert.That(deserializedBinaryKV3.Data.Root["emptyBlob"].AsBlob(), Is.Empty);
            }
        }

        [Test]
        public void TestBinaryKV3Version5NonObjectRoot()
        {
            var root = KVObject.Array();
            root.Add(42);
            root.Add("root array");
            root.Add(KVObject.Collection());
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = KV3BinaryVersion.Version5,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);

            Assert.That(deserializedBinaryKV3.Data.Root.IsArray, Is.True);
            Assert.That((int)deserializedBinaryKV3.Data.Root[0]!, Is.EqualTo(42));
            Assert.That((string)deserializedBinaryKV3.Data.Root[1]!, Is.EqualTo("root array"));
            Assert.That(deserializedBinaryKV3.Data.Root[2], Is.Empty);
        }

        [Test]
        public void TestBinaryKV3Version5Lz4BlobFramesRespectBlobBoundaries()
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
                SerializationVersion = KV3BinaryVersion.Version5,
                SerializationCompressionMethod = KV3BinaryCompressionMethod.Lz4,
            };

            using var stream = new MemoryStream();
            binaryKV3.Serialize(stream);
            var data = stream.ToArray();

            Assert.That(BitConverter.ToInt32(data, 68), Is.EqualTo(4 * sizeof(ushort)));

            stream.Position = 0;
            var deserializedBinaryKV3 = new BinaryKV3(BlockType.DATA)
            {
                Size = (uint)stream.Length,
                Offset = 0,
                Resource = null!,
            };
            using var reader = new BinaryReader(stream);
            deserializedBinaryKV3.Read(reader);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(deserializedBinaryKV3.Data.Root[0]!.AsBlob(), Is.EqualTo(firstBlob));
                Assert.That(deserializedBinaryKV3.Data.Root[1]!.AsBlob(), Is.EqualTo(secondBlob));
                Assert.That(deserializedBinaryKV3.Data.Root[2]!.AsBlob(), Is.EqualTo(thirdBlob));
            }
        }

        [Test]
        public void TestBinaryKV3SerializationDefaultsAndValidation()
        {
            var root = KVObject.Collection();
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic")) { Resource = null! };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(binaryKV3.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version4));
                Assert.That(binaryKV3.SerializationCompressionMethod, Is.EqualTo(KV3BinaryCompressionMethod.Uncompressed));
            }

            binaryKV3.SerializationVersion = (KV3BinaryVersion)99;
            Assert.That(() => binaryKV3.Serialize(new MemoryStream()), Throws.TypeOf<NotSupportedException>());

            binaryKV3.SerializationVersion = KV3BinaryVersion.Version5;
            binaryKV3.SerializationCompressionMethod = (KV3BinaryCompressionMethod)99;
            Assert.That(() => binaryKV3.Serialize(new MemoryStream()), Throws.TypeOf<NotSupportedException>());
        }

        [TestCase("ar_dizzy_kv3_v3_uncompressed.vpost_c", BlockType.DATA, KV3BinaryCompressionMethod.Uncompressed)]
        [TestCase("aw_ti9_gargoyle_collision_kv3_v3_zstd.vmdl_c", BlockType.ANIM, KV3BinaryCompressionMethod.Zstd)]
        [TestCase("compute_reactive_mask_kv3_v3_lz4.vmat_c", BlockType.DATA, KV3BinaryCompressionMethod.Lz4)]
        [TestCase("panorama_world_panel_default_kv3_v3_lz4.vmat_c", BlockType.DATA, KV3BinaryCompressionMethod.Lz4)]
        [TestCase("piece_kv3_v4.vmdl_c", BlockType.PHYS, KV3BinaryCompressionMethod.Lz4)]
        [TestCase("default_ents_kv3_v4_zstd.vents_c", BlockType.DATA, KV3BinaryCompressionMethod.Zstd)]
        public void TestBinaryKV3SourceCompressionIsPreserved(
            string fileName,
            BlockType blockType,
            KV3BinaryCompressionMethod expectedCompressionMethod)
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", fileName);
            using var resource = new Resource();
            resource.Read(file);

            var block = resource.Blocks.Single(block => block.Type == blockType);
            using var sourceStream = File.OpenRead(file);
            var binaryKV3 = ReadBinaryKV3Block(sourceStream, block);
            using var output = new MemoryStream();
            binaryKV3.Serialize(output);
            var serializedData = output.ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(binaryKV3.SerializationCompressionMethod, Is.EqualTo(expectedCompressionMethod));
                Assert.That(BitConverter.ToUInt32(serializedData, 20), Is.EqualTo((uint)expectedCompressionMethod));
            }
        }

        [Test]
        public void TestBinaryKV3Version5ZstdSourceSettingsArePreserved()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "abilities_kv3_v5_zstd.vdata_c");
            using var resource = new Resource();
            resource.Read(file);
            var found = false;

            foreach (var block in resource.Blocks)
            {
                if (block is not BinaryKV3 binaryKV3 || binaryKV3.SerializationVersion != KV3BinaryVersion.Version5)
                {
                    continue;
                }

                found = true;
                Assert.That(binaryKV3.SerializationCompressionMethod, Is.EqualTo(KV3BinaryCompressionMethod.Zstd));
                var reparsed = RoundTrip(binaryKV3);
                Assert.That(reparsed.SerializationCompressionMethod, Is.EqualTo(KV3BinaryCompressionMethod.Zstd));
                Assert.That(reparsed.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version5));
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void TestDeadlockBinaryKV3Version5SourceSettingsArePreserved()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "deadlock_tracked_stats_player_staging_kv3_v5.vdata_c");
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
                    Assert.That(sourceBinaryKV3.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version5), block.Type.ToString());
                    Assert.That(sourceBinaryKV3.SerializationCompressionMethod, Is.EqualTo(expectedCompression), block.Type.ToString());
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

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(binaryKV3.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version5), block.Type.ToString());
                    Assert.That(binaryKV3.SerializationCompressionMethod, Is.EqualTo(expectedCompression), block.Type.ToString());
                    Assert.That(binaryKV3.Data.ToKV3String(), Is.EqualTo(expectedText[block.Type]), block.Type.ToString());
                }
            }

            Assert.That(found, Is.EqualTo(expectedCompressions.Count));
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

        private static IEnumerable<TestCaseData> BinaryKV3Version4FixtureCases()
        {
            var fixtures = new[]
            {
                ("basepostprocess_kv3_v4_uncompressed.vpost_c", BlockType.DATA, 1, 131072),
                ("piece_kv3_v4.vmdl_c", BlockType.PHYS, 7, 1378),
            };

            foreach (var (fileName, blockType, blobCount, blobBytes) in fixtures)
            {
                foreach (var compressionMethod in Enum.GetValues<KV3BinaryCompressionMethod>())
                {
                    yield return new TestCaseData(fileName, blockType, blobCount, blobBytes, compressionMethod)
                        .SetName($"{{m}}({Path.GetFileNameWithoutExtension(fileName)}, {compressionMethod})");
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

        private static void AssertKV3Properties(KVDocument file)
        {
            using (Assert.EnterMultipleScope())
            {
                //Not sure what KVType is better for this
                Assert.That((string)file.Root["multiLineStringValue"], Is.EqualTo("First line of a multi-line string literal.\nSecond line of a multi-line string literal."));

                Assert.That(file.Header!.Format.ToString(), Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

                Assert.That(file.Root, Has.Count.EqualTo(14));

                Assert.That(file.Root["boolValue"].ValueType, Is.EqualTo(KVValueType.Boolean));
                Assert.That((bool)file.Root["boolValue"], Is.False);
                Assert.That(file.Root["intValue"].ValueType, Is.EqualTo(KVValueType.UInt64));
                Assert.That((ulong)file.Root["intValue"], Is.EqualTo((ulong)128));
                Assert.That(file.Root["doubleValue"].ValueType, Is.EqualTo(KVValueType.FloatingPoint64));
                Assert.That((double)file.Root["doubleValue"], Is.EqualTo(64.000000));
                Assert.That(file.Root["negativeIntValue"].ValueType, Is.EqualTo(KVValueType.Int64));
                Assert.That((long)file.Root["negativeIntValue"], Is.EqualTo((long)-1337));
                Assert.That(file.Root["negativeDoubleValue"].ValueType, Is.EqualTo(KVValueType.FloatingPoint64));
                Assert.That((double)file.Root["negativeDoubleValue"], Is.EqualTo(-0.133700));
                Assert.That(file.Root["stringValue"].ValueType, Is.EqualTo(KVValueType.String));
                Assert.That((string)file.Root["stringValue"], Is.EqualTo("hello world"));

                //Do special test for flagged value
                var flagValue = file.Root["stringThatIsAResourceReference"];
                Assert.That((string)flagValue, Is.EqualTo("particles/items3_fx/star_emblem.vpcf"));
                Assert.That(flagValue.Flag, Is.EqualTo(KVFlag.Resource));

                Assert.That(file.Root["arrayValue"].ValueType, Is.EqualTo(KVValueType.Array));
                var arrayValue = file.Root["arrayValue"];
                Debug.Assert(arrayValue != null);
                Assert.That((ulong)arrayValue[0]!, Is.EqualTo((ulong)1));
                Assert.That((ulong)arrayValue[1]!, Is.EqualTo((ulong)2));
                Assert.That((string)arrayValue[2]!, Is.EqualTo("characters/models/shared/animsets/animset_ct.vmdl"));
                Assert.That(arrayValue[2]!.Flag, Is.EqualTo(KVFlag.Resource));
                Assert.That((string)arrayValue[3]!, Is.EqualTo("hud/abilities/haze/haze_sleep_dagger.psd"));
                Assert.That(arrayValue[3]!.Flag, Is.EqualTo(KVFlag.Panorama));
                Assert.That((string)arrayValue[4]!, Is.EqualTo("hello world"));
                Assert.That(arrayValue[5]!.Flag, Is.EqualTo(KVFlag.SoundEvent));
                Assert.That(arrayValue[6]!.Flag, Is.EqualTo(KVFlag.SubClass));
                Assert.That(arrayValue[7]!.Flag, Is.EqualTo(KVFlag.EntityName));

                Assert.That(file.Root["objectValue"].ValueType, Is.EqualTo(KVValueType.Collection));
                var objectValue = file.Root["objectValue"];
                Debug.Assert(objectValue != null);
                Assert.That((ulong)objectValue["n"], Is.EqualTo((ulong)5));
                Assert.That((string)objectValue["s"], Is.EqualTo("foo"));

                var binaryBlobValue = file.Root["binaryBlobValue"];
                Assert.That(binaryBlobValue.ValueType, Is.EqualTo(KVValueType.BinaryBlob));
                Assert.That(binaryBlobValue.AsBlob(), Has.Length.EqualTo(40));
                Assert.That(Encoding.UTF8.GetString(binaryBlobValue.AsBlob()), Is.EqualTo("Hello, this is a test binary blob value!"));

                Assert.That(file.Root["arrayOnSingleLine"].ValueType, Is.EqualTo(KVValueType.Array));

                Assert.That((string)file.Root["quoted.key"], Is.EqualTo("hello"));
                Assert.That((string)file.Root["a quoted key with spaces"], Is.EqualTo("some cool value"));
            }
        }

        [Test]
        public void TestKV3Guids()
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, guid) in KV3IDLookup.Table)
                {
                    if (name == "vpcf38") // Classic valve
                    {
                        Assert.That(guid.Version, Is.EqualTo(1), name);
                        continue;
                    }

                    Assert.That(guid.Version, Is.EqualTo(4), name);
                }
            }
        }

        [Test]
        public void TestKV3StringEscaping()
        {
            var expectedFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "StringEscaping.kv3");

            var parsedFile = KVDocumentExtensions.ParseKV3(expectedFilePath);
            var serializedOutput = parsedFile.ToKV3String().Trim().ReplaceLineEndings();
            var expectedOutput = File.ReadAllText(expectedFilePath).Trim().ReplaceLineEndings();

            Assert.That(serializedOutput, Is.EqualTo(expectedOutput));
        }

    }
}
