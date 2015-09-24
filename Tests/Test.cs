using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class Test
    {
        private List<Resource> Resources = new List<Resource>();

        [SetUp]
        public void SetUp()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Assert.Fail("There are no files to test.");
            }

            foreach (var file in files)
            {
                Console.WriteLine("Reading \"{0}\"...", file);

                var resource = new Resource();
                resource.Read(file);

                Resources.Add(resource);

                Console.WriteLine("\tOK");
            }
        }

        [Test]
        public void TestCase()
        {
            
        }
    }
}
