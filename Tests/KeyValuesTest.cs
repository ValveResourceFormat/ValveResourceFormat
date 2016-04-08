using System.IO;
using NUnit.Framework;
using ValveResourceFormat.KeyValues;

namespace Tests
{
    [TestFixture]
    public class KeyValuesTest
    {
        [Test]
        public void TestKeyValues3_LF()
        {
            var file = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_LF.kv3"));

            //Not sure what KVType is better for this
            Assert.AreEqual("First line of a multi-line string literal.\nSecond line of a multi-line string literal.",
                file.Root.Properties["multiLineStringValue"].Value);

            TestKeyValues3(file);
        }
        [Test]
        public void TestKeyValues3_CRLF()
        {
            var file = KeyValues3.ParseKVFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "KeyValues", "KeyValues3_CRLF.kv3"));

            //Not sure what KVType is better for this
            Assert.AreEqual("First line of a multi-line string literal.\r\nSecond line of a multi-line string literal.",
                file.Root.Properties["multiLineStringValue"].Value);

            TestKeyValues3(file);
        }

        public void TestKeyValues3(KV3File file)
        {
            Assert.AreEqual("text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}", file.Encoding);
            Assert.AreEqual("generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}", file.Format);

            Assert.AreEqual(8, file.Root.Count);

            var properties = file.Root.Properties;

            Assert.AreEqual(KVType.BOOLEAN, properties["boolValue"].Type);
            Assert.AreEqual(false, properties["boolValue"].Value);
            Assert.AreEqual(KVType.INTEGER, properties["intValue"].Type);
            Assert.AreEqual((long)128, properties["intValue"].Value);
            Assert.AreEqual(KVType.DOUBLE, properties["doubleValue"].Type);
            Assert.AreEqual(64.000000, properties["doubleValue"].Value);
            Assert.AreEqual(KVType.STRING, properties["stringValue"].Type);
            Assert.AreEqual("hello world", properties["stringValue"].Value);

            //Do special test for flagged value
            var flagValue = properties["stringThatIsAResourceReference"] as KVFlaggedValue;
            Assert.AreEqual("particles/items3_fx/star_emblem.vpcf", flagValue.Value);
            Assert.AreEqual(KVFlag.Resource, flagValue.Flag);

            Assert.AreEqual(KVType.ARRAY, properties["arrayValue"].Type);
            var arrayValue = properties["arrayValue"].Value as KVObject;
            Assert.AreEqual((long)1, arrayValue.Properties["0"].Value);
            Assert.AreEqual((long)2, arrayValue.Properties["1"].Value);

            Assert.AreEqual(KVType.OBJECT, properties["objectValue"].Type);
            var objectValue = properties["objectValue"].Value as KVObject;
            Assert.AreEqual((long)5, objectValue.Properties["n"].Value);
            Assert.AreEqual("foo", objectValue.Properties["s"].Value);
        }
    }
}
