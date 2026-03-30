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
            var file = KV3File.Parse(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            Assert.That(file.Encoding.ToString(), Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
            AssertKV3Properties(file);
        }

        [Test]
        public void TestBinaryKV3_Serialization()
        {
            var originalFile = KV3File.Parse(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));

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

            var deserializedFile = deserializedBinaryKV3.GetKV3File();
            AssertKV3Properties(deserializedFile);
        }

        private static void AssertKV3Properties(KV3File file)
        {
            using (Assert.EnterMultipleScope())
            {
                //Not sure what KVType is better for this
                Assert.That((string)file.Root["multiLineStringValue"], Is.EqualTo("First line of a multi-line string literal.\nSecond line of a multi-line string literal."));

                Assert.That(file.Format.ToString(), Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

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
                var arrayValue = file.Root.GetChild("arrayValue");
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
                var objectValue = file.Root.GetChild("objectValue");
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

            var parsedFile = KV3File.Parse(expectedFilePath);
            var serializedOutput = parsedFile.ToString().Trim().ReplaceLineEndings();
            var expectedOutput = File.ReadAllText(expectedFilePath).Trim().ReplaceLineEndings();

            Assert.That(serializedOutput, Is.EqualTo(expectedOutput));
        }

    }
}
