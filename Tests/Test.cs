using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using System.Text.RegularExpressions;

namespace Tests
{
    [TestFixture]
    public class Test
    {
        private Dictionary<string, Resource> Resources;

        public Test()
        {
            Resources = new Dictionary<string, Resource>();
        }

        [SetUp]
        public void SetUp()
        {
            Console.WriteLine(Environment.NewLine + "Setting up resource tests...");

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

                Resources.Add(Path.GetFileName(file), resource);

                Console.WriteLine("\tOK");
            }
        }

        [Test]
        public void VerifyBlocks()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput");
            var files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (!Resources.ContainsKey(name))
                {
                    Console.WriteLine("{0}: no such resource", name);

                    continue;
                }

                var resource = Resources[name];
                var blockName = Path.GetFileNameWithoutExtension(file);

                BlockType blockType;
                Enum.TryParse(blockName, false, out blockType);

                if (!resource.Blocks.ContainsKey(blockType))
                {
                    Console.WriteLine("{0}: no such block: {1}", name, blockType);

                    continue;
                }

                Console.WriteLine("{0}: Testing {1} block...", name, blockType);

                var actualOutput = resource.Blocks[blockType].ToString();
                var expectedOutput = File.ReadAllText(file);

                // We don't care about Valve's messy whitespace, so just strip it.
                actualOutput = Regex.Replace(actualOutput, @"\s+", String.Empty);
                expectedOutput = Regex.Replace(expectedOutput, @"\s+", String.Empty);

                Assert.AreEqual(actualOutput, expectedOutput);
            }
        }
    }
}
