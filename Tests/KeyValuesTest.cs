using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using KVValueType = ValveKeyValue.KVValueType;

namespace Tests
{
    [TestFixture]
    public class KeyValuesTest
    {
        [Test]
        public void TestKeyValues3_CRLF()
        {
            var file = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_CRLF.kv3"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(file.Encoding.ToString(), Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
                Assert.That(file.Format.ToString(), Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

                //Not sure what KVType is better for this
                Assert.That(file.Root.Properties["multiLineStringValue"].Value, Is.EqualTo("First line of a multi-line string literal.\r\nSecond line of a multi-line string literal."));
            }
        }

        [Test]
        public void TestKeyValues3_LF()
        {
            var file = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));
            Assert.That(file.Encoding.ToString(), Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
            AssertKV3Properties(file);
        }

        [Test]
        public void TestBinaryKV3_Serialization()
        {
            var originalFile = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));

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
                Assert.That(file.Root.Properties["multiLineStringValue"].Value, Is.EqualTo("First line of a multi-line string literal.\nSecond line of a multi-line string literal."));

                Assert.That(file.Format.ToString(), Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

                Assert.That(file.Root, Has.Count.EqualTo(14));

                var properties = file.Root.Properties;

                Assert.That(properties["boolValue"].Type, Is.EqualTo(KVValueType.Boolean));
                Assert.That(properties["boolValue"].Value, Is.False);
                Assert.That(properties["intValue"].Type, Is.EqualTo(KVValueType.Int64));
                Assert.That(properties["intValue"].Value, Is.EqualTo((long)128));
                Assert.That(properties["doubleValue"].Type, Is.EqualTo(KVValueType.FloatingPoint64));
                Assert.That(properties["doubleValue"].Value, Is.EqualTo(64.000000));
                Assert.That(properties["negativeIntValue"].Type, Is.EqualTo(KVValueType.Int64));
                Assert.That(properties["negativeIntValue"].Value, Is.EqualTo((long)-1337));
                Assert.That(properties["negativeDoubleValue"].Type, Is.EqualTo(KVValueType.FloatingPoint64));
                Assert.That(properties["negativeDoubleValue"].Value, Is.EqualTo(-0.133700));
                Assert.That(properties["stringValue"].Type, Is.EqualTo(KVValueType.String));
                Assert.That(properties["stringValue"].Value, Is.EqualTo("hello world"));

                //Do special test for flagged value
                var flagValue = properties["stringThatIsAResourceReference"];
                Assert.That(flagValue.Value, Is.EqualTo("particles/items3_fx/star_emblem.vpcf"));
                Assert.That(flagValue.Flag, Is.EqualTo(KVFlag.Resource));

                Assert.That(properties["arrayValue"].Type, Is.EqualTo(KVValueType.Array));
                var arrayValue = properties["arrayValue"].Value as KVObject;
                Debug.Assert(arrayValue != null);
                Assert.That(arrayValue.Properties["0"].Value, Is.EqualTo((long)1));
                Assert.That(arrayValue.Properties["1"].Value, Is.EqualTo((long)2));
                Assert.That(arrayValue.Properties["2"].Value, Is.EqualTo("characters/models/shared/animsets/animset_ct.vmdl"));
                Assert.That(arrayValue.Properties["2"].Flag, Is.EqualTo(KVFlag.Resource));
                Assert.That(arrayValue.Properties["3"].Value, Is.EqualTo("hud/abilities/haze/haze_sleep_dagger.psd"));
                Assert.That(arrayValue.Properties["3"].Flag, Is.EqualTo(KVFlag.Panorama));
                Assert.That(arrayValue.Properties["4"].Value, Is.EqualTo("hello world"));
                Assert.That(arrayValue.Properties["5"].Flag, Is.EqualTo(KVFlag.SoundEvent));
                Assert.That(arrayValue.Properties["6"].Flag, Is.EqualTo(KVFlag.SubClass));
                Assert.That(arrayValue.Properties["7"].Flag, Is.EqualTo(KVFlag.EntityName));

                Assert.That(properties["objectValue"].Type, Is.EqualTo(KVValueType.Collection));
                var objectValue = properties["objectValue"].Value as KVObject;
                Debug.Assert(objectValue != null);
                Assert.That(objectValue.Properties["n"].Value, Is.EqualTo((long)5));
                Assert.That(objectValue.Properties["s"].Value, Is.EqualTo("foo"));

                var binaryBlobValue = properties["binaryBlobValue"].Value as byte[];
                Debug.Assert(binaryBlobValue != null);
                Assert.That(binaryBlobValue, Has.Length.EqualTo(40));
                Assert.That(Encoding.UTF8.GetString(binaryBlobValue), Is.EqualTo("Hello, this is a test binary blob value!"));

                Assert.That(properties["arrayOnSingleLine"].Type, Is.EqualTo(KVValueType.Array));

                Assert.That(properties["quoted.key"].Value, Is.EqualTo("hello"));
                Assert.That(properties["a quoted key with spaces"].Value, Is.EqualTo("some cool value"));
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
        public void TestKV3HeaderParsing()
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);

            writer.WriteLine("<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->");
            writer.WriteLine("{");
            writer.WriteLine("    testKey = \"testValue\"");
            writer.WriteLine("}");
            writer.Flush();

            stream.Position = 0;
            var file = KeyValues3.ParseKVFile(stream);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(file.Encoding.Name, Is.EqualTo("text"));
                Assert.That(file.Encoding.Id, Is.EqualTo(Guid.Parse("e21c7f3c-8a33-41c5-9977-a76d3a32aa0d")));
                Assert.That(file.Format.Name, Is.EqualTo("generic"));
                Assert.That(file.Format.Id, Is.EqualTo(Guid.Parse("7412167c-06e9-4698-aff2-e63eb59037e7")));
            }
        }

        [Test]
        public void TestKV3HeaderParsing_PartialHeader()
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);

            writer.WriteLine("<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->");
            writer.WriteLine("{");
            writer.WriteLine("    testKey = \"testValue\"");
            writer.WriteLine("}");
            writer.Flush();

            stream.Position = 0;
            var file = KeyValues3.ParseKVFile(stream);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(file.Encoding, Is.EqualTo(KV3IDLookup.Get("text")));
                Assert.That(file.Format, Is.EqualTo(KV3IDLookup.Get("generic")));
            }
        }

        [Test]
        public void TestKV3HeaderParsing_CustomFormat()
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);

            writer.WriteLine("<!-- kv3 encoding:binary:version{12345678-1234-5678-9abc-123456789abc} format:custom:version{87654321-4321-8765-cba9-987654321abc} -->");
            writer.WriteLine("{");
            writer.WriteLine("    testKey = \"testValue\"");
            writer.WriteLine("}");
            writer.Flush();

            stream.Position = 0;
            var file = KeyValues3.ParseKVFile(stream);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(file.Encoding.Name, Is.EqualTo("binary"));
                Assert.That(file.Encoding.Id, Is.EqualTo(Guid.Parse("12345678-1234-5678-9abc-123456789abc")));
                Assert.That(file.Format.Name, Is.EqualTo("custom"));
                Assert.That(file.Format.Id, Is.EqualTo(Guid.Parse("87654321-4321-8765-cba9-987654321abc")));
            }
        }

        [Test]
        public void TestKV3StringEscaping()
        {
            var expectedFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "StringEscaping.kv3");

            var parsedFile = KeyValues3.ParseKVFile(expectedFilePath);
            var serializedOutput = parsedFile.ToString().Trim().ReplaceLineEndings();
            var expectedOutput = File.ReadAllText(expectedFilePath).Trim().ReplaceLineEndings();

            Assert.That(serializedOutput, Is.EqualTo(expectedOutput));
        }

        [Test]
        public void TestEscapedFakeMultiline()
        {
            var expectedFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "StringEscaping_LF.kv3");

            var parsedFile = KeyValues3.ParseKVFile(expectedFilePath);
            Assert.That(parsedFile.Root.Properties["with_quote_at_start"].Value, Is.EqualTo("\""));
        }

        [Test]
        public void TestManualKVObjectSerializationWithEscapeSequences()
        {
            var inputFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "EscapeSequenceTest_Input.kv3");
            var parsedFile = KeyValues3.ParseKVFile(inputFilePath);
            var serializedOutput = parsedFile.ToString().Trim().ReplaceLineEndings();

            var expectedFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "EscapeSequenceTest.kv3");
            var expectedOutput = File.ReadAllText(expectedFilePath).Trim().ReplaceLineEndings();

            Assert.That(serializedOutput, Is.EqualTo(expectedOutput));
        }
    }
}
