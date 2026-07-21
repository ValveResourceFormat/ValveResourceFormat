using System.Diagnostics;
using System.IO;
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
                Assert.That((string)deserializedBinaryKV3.Data.Root["emptyString"], Is.Empty);
                Assert.That(deserializedBinaryKV3.Data.Root["emptyArray"], Is.Empty);
                Assert.That(deserializedBinaryKV3.Data.Root["emptyObject"], Is.Empty);
            }
        }

        [TestCase(KV3BinaryCompressionMethod.Uncompressed)]
        [TestCase(KV3BinaryCompressionMethod.Lz4)]
        [TestCase(KV3BinaryCompressionMethod.Zstd)]
        public void TestBinaryKV3Version5EmptyBlob(KV3BinaryCompressionMethod compressionMethod)
        {
            var root = KVObject.Collection();
            root["emptyBlob"] = KVObject.Blob([]);
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic"))
            {
                Resource = null!,
                SerializationVersion = KV3BinaryVersion.Version5,
                SerializationCompressionMethod = compressionMethod,
            };

            var deserializedBinaryKV3 = RoundTrip(binaryKV3);
            Assert.That(deserializedBinaryKV3.Data.Root["emptyBlob"].AsBlob(), Is.Empty);
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
        public void TestBinaryKV3SerializationDefaultsAndValidation()
        {
            var root = KVObject.Collection();
            var binaryKV3 = new BinaryKV3(root, KV3IDLookup.Get("generic")) { Resource = null! };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(binaryKV3.SerializationVersion, Is.EqualTo(KV3BinaryVersion.Version4));
                Assert.That(binaryKV3.SerializationCompressionMethod, Is.EqualTo(KV3BinaryCompressionMethod.Uncompressed));
            }

            binaryKV3.SerializationCompressionMethod = KV3BinaryCompressionMethod.Lz4;
            Assert.That(() => binaryKV3.Serialize(new MemoryStream()), Throws.TypeOf<NotSupportedException>());

            binaryKV3.SerializationVersion = (KV3BinaryVersion)99;
            Assert.That(() => binaryKV3.Serialize(new MemoryStream()), Throws.TypeOf<NotSupportedException>());

            binaryKV3.SerializationVersion = KV3BinaryVersion.Version5;
            binaryKV3.SerializationCompressionMethod = (KV3BinaryCompressionMethod)99;
            Assert.That(() => binaryKV3.Serialize(new MemoryStream()), Throws.TypeOf<NotSupportedException>());
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
