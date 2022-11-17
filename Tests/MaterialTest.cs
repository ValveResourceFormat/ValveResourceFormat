using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class MaterialTest
    {
        [Test]
        public void TestMaterial()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "point_worldtext_default.vmat_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var materialExtract = new MaterialExtract(resource);

            Assert.That(materialExtract.ToValveMaterial(), Is.Not.Empty);
        }
    }
}
