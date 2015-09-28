using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class PackageTest
    {
        [SetUp]
        public void SetUp()
        {
            Console.WriteLine(Environment.NewLine + "Setting up package tests...");
        }
        
        [Test]
        public void ParseVPK()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "platform_misc_dir.vpk");

            Console.WriteLine("Reading \"{0}\"...", path);

            var package = new Package();
            package.Read(path);

            Console.WriteLine("\tOK");
        }
    }
}
