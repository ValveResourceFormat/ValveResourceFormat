using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class PackageTest
    {
        [Test]
        public void ParseVPK()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "platform_misc_dir.vpk");

            using (var package = new Package())
            {
                package.Read(path);
            }
        }

        [Test]
        public void InvalidPackageThrows()
        {
            using (var resource = new Package())
            {
                using (var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray()))
                {
                    // Should yell about not setting file name
                    Assert.Throws<InvalidOperationException>(() => resource.Read(ms));

                    resource.SetFileName("a.vpk");

                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }

        [Test]
        public void CorrectHeaderWrongVersionThrows()
        {
            using (var resource = new Package())
            {
                resource.SetFileName("a.vpk");

                using (var ms = new MemoryStream(new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x11, 0x11, 0x11, 0x11, 0x22, 0x22, 0x22, 0x22 }))
                {
                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }
    }
}
