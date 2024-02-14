using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Utils;

namespace Tests
{
    [TestFixture]
    public partial class Test
    {
        // TODO: Add asserts for blocks/resources that were skipped

        [Test]
        public void ReadBlocks()
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
                using var resource = new Resource
                {
                    FileName = file,
                };
                resource.Read(file);

                resources.Add(Path.GetFileName(file), resource);

                Assert.That(resource.ResourceType, Is.Not.EqualTo(ResourceType.Unknown));

                // Verify extension
                var extension = Path.GetExtension(file);

                if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    extension = extension[..^2];
                }

                var attribute = "." + resource.ResourceType.GetExtension();
                Assert.That(attribute, Is.EqualTo(extension), file);

                if (resource.ResourceType != ResourceType.Map) /// Tested by <see cref="MapExtractTest"/>
                {
                    InternalTestExtraction.Test(resource);
                }
            }

            Assert.Multiple(() => VerifyResources(resources));
        }

        [Test]
        public void ReadBlocksWithMemoryStream()
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
                using var resource = new Resource
                {
                    FileName = file,
                };

                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);

                resource.Read(ms);
            }
        }

        static void VerifyResources(Dictionary<string, Resource> resources)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput");
            var files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);
            var exceptions = new StringBuilder();

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (!resources.TryGetValue(name, out var resource))
                {
                    Assert.Fail($"{name}: no such resource");

                    continue;
                }

                var blockName = Path.GetFileNameWithoutExtension(file);

                Enum.TryParse(blockName, false, out BlockType blockType);

                if (!resource.ContainsBlockType(blockType))
                {
                    Assert.Fail($"{name}: no such block: {blockType}");

                    continue;
                }

                var actualOutput = resource.GetBlockByType(blockType).ToString();
                var expectedOutput = File.ReadAllText(file);

                // We don't care about Valve's messy whitespace, so just strip it.
                actualOutput = SpaceRegex().Replace(actualOutput, string.Empty);

                expectedOutput = expectedOutput.Replace("Source 2 Viewer - https://valveresourceformat.github.io", StringToken.VRF_GENERATOR, StringComparison.Ordinal);
                expectedOutput = SpaceRegex().Replace(expectedOutput, string.Empty);

                //Assert.That(actualOutput, Is.EqualTo(expectedOutput));
                if (expectedOutput != actualOutput)
                {
                    TestContext.Error.WriteLine($"File '{file}' has mismatching ToString() in {blockType}");
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
            using var resource = new Resource();
            using var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray());

            Assert.Throws<UnexpectedMagicException>(() => resource.Read(ms));
        }

        [Test]
        public void PackageInResourceThrows()
        {
            var data = new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x00, 0x00 };

            using var resource = new Resource();
            using var ms = new MemoryStream(data);

            var ex = Assert.Throws<InvalidDataException>(() => resource.Read(ms));

            Assert.That(ex.Message, Does.Contain("Use ValvePak"));
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex SpaceRegex();
    }
}
