using System.IO;
using System.Text;
using NUnit.Framework;
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

            Assert.Multiple(() =>
            {
                Assert.That(file.Encoding, Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
                Assert.That(file.Format, Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

                //Not sure what KVType is better for this
                Assert.That(file.Root.Properties["multiLineStringValue"].Value, Is.EqualTo("First line of a multi-line string literal.\r\nSecond line of a multi-line string literal."));
            });
        }

        [Test]
        public void TestKeyValues3_LF()
        {
            var file = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));

            Assert.Multiple(() =>
            {
                //Not sure what KVType is better for this
                Assert.That(file.Root.Properties["multiLineStringValue"].Value, Is.EqualTo("First line of a multi-line string literal.\nSecond line of a multi-line string literal."));

                Assert.That(file.Encoding, Is.EqualTo("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}"));
                Assert.That(file.Format, Is.EqualTo("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}"));

                Assert.That(file.Root, Has.Count.EqualTo(14));

                var properties = file.Root.Properties;

                Assert.That(properties["boolValue"].Type, Is.EqualTo(KVValueType.Boolean));
                Assert.That(properties["boolValue"].Value, Is.EqualTo(false));
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
                Assert.That(arrayValue.Properties["0"].Value, Is.EqualTo((long)1));
                Assert.That(arrayValue.Properties["1"].Value, Is.EqualTo((long)2));
                Assert.That(arrayValue.Properties["2"].Value, Is.EqualTo("characters/models/shared/animsets/animset_ct.vmdl"));
                Assert.That(arrayValue.Properties["3"].Value, Is.EqualTo("hud/abilities/haze/haze_sleep_dagger.psd"));
                Assert.That(arrayValue.Properties["4"].Value, Is.EqualTo("hello world"));

                Assert.That(properties["objectValue"].Type, Is.EqualTo(KVValueType.Collection));
                var objectValue = properties["objectValue"].Value as KVObject;
                Assert.That(objectValue.Properties["n"].Value, Is.EqualTo((long)5));
                Assert.That(objectValue.Properties["s"].Value, Is.EqualTo("foo"));

                var binaryBlobValue = properties["binaryBlobValue"].Value as byte[];
                Assert.That(binaryBlobValue, Has.Length.EqualTo(40));
                Assert.That(Encoding.UTF8.GetString(binaryBlobValue), Is.EqualTo("Hello, this is a test binary blob value!"));

                Assert.That(properties["arrayOnSingleLine"].Type, Is.EqualTo(KVValueType.Array));

                Assert.That(properties["quoted.key"].Value, Is.EqualTo("hello"));
                Assert.That(properties["a quoted key with spaces"].Value, Is.EqualTo("some cool value"));
            });
        }
    }
}
