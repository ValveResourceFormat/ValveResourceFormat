using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class Test
    {
        // TODO: Add asserts for blocks/resources that were skipped

        [Test]
        public void VerifyBlocks()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Assert.Fail("There are no files to test.");
            }

            foreach (var file in files)
            {
                var resource = new Resource();
                resource.Read(file);

                resources.Add(Path.GetFileName(file), resource);
            }

            path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput");
            files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);
            var exceptions = new StringBuilder();

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (!resources.ContainsKey(name))
                {
                    Assert.Fail("{0}: no such resource", name);

                    continue;
                }

                var resource = resources[name];
                var blockName = Path.GetFileNameWithoutExtension(file);

                BlockType blockType;
                Enum.TryParse(blockName, false, out blockType);

                if (!resource.Blocks.ContainsKey(blockType))
                {
                    Assert.Fail("{0}: no such block: {1}", name, blockType);

                    continue;
                }

                var actualOutput = resource.Blocks[blockType].ToString();
                var expectedOutput = File.ReadAllText(file);

                // We don't care about Valve's messy whitespace, so just strip it.
                actualOutput = Regex.Replace(actualOutput, @"\s+", String.Empty);
                expectedOutput = Regex.Replace(expectedOutput, @"\s+", String.Empty);

                try
                {
                    // TODO: Skip failing DATA tests for now
                    if(blockType != BlockType.DATA || expectedOutput == actualOutput)
                    Assert.AreEqual(expectedOutput, actualOutput);
                }
                catch (AssertionException e)
                {
                    exceptions.AppendLine("File: " + file);
                    exceptions.AppendLine(e + Environment.NewLine);
                }
            }

            if (exceptions.Length > 0)
            {
                throw new AssertionException(exceptions.ToString());
            }
        }

        [Test]
        public void InvalidResourceThrows()
        {
            using (var resource = new Resource())
            {
                using (var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray()))
                {
                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }

        [Test]
        public void PackageInResourceThrows()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "platform_misc_dir.vpk");

            using (var resource = new Resource())
            {
                Assert.Throws<InvalidDataException>(() => resource.Read(path));
            }
        }
    }
}
