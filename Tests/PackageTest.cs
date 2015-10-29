using System;
using System.IO;
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

            var package = new Package();
            package.Read(path);
        }
    }
}
