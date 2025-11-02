using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
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
            var files = Directory.GetFiles(path, "*.*_c", new EnumerationOptions
            {
                RecurseSubdirectories = true,
            });

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

                try
                {
                    resource.Read(file);
                }
                catch (NotImplementedException e) when (e.Message == "More than one indirection, not yet handled.")
                {
                    Console.WriteLine(e);
                    continue;
                }

                resources.Add(Path.GetFileName(file), resource);

                Assert.That(resource.ResourceType, Is.Not.EqualTo(ResourceType.Unknown));
                Assert.That(resource.ResourceType, Is.EqualTo(ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(file.AsSpan()))));


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

            VerifyResources(resources);
        }

        [Test]
        public void RoundtripSerialization()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files");
            var files = Directory.GetFiles(path, "*.*_c", new EnumerationOptions
            {
                RecurseSubdirectories = true,
            });
            var total = 0;
            var notImplemented = 0;

            if (files.Length == 0)
            {
                Assert.Fail("There are no files to test.");
            }

            foreach (var file in files)
            {
                var ms = new MemoryStream();

                using (var resourceOnDisk = new Resource
                {
                    FileName = file,
                })
                {
                    try
                    {
                        resourceOnDisk.Read(file);
                    }
                    catch (NotImplementedException)
                    {
                        continue;
                    }

                    total++;

                    try
                    {
                        resourceOnDisk.Serialize(ms);
                    }
                    catch (NotImplementedException)
                    {
                        notImplemented++;
                        continue;
                    }
                }

                ms.Position = 0;

                // Now try to parse what we just wrote
                using var resource = new Resource
                {
                    FileName = file,
                };
                resource.Read(ms);

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

            VerifyResources(resources, validateMissingResources: false);

            Console.WriteLine($"{notImplemented} out of {total} files are not yet serializable.");
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

                VerifyDataBlock(resource, file);
            }
        }

        [Test]
        public void ReadBlocksNoFileName()
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
                using var resource = new Resource();
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                resource.Read(fs);

                VerifyDataBlock(resource, file);
            }
        }

        private static readonly HashSet<string> FilesWithEmptyDataBlocks =
        [
            "dota.vmap_c",
            "empty_data.vjs_c",
            "sbox_visualize_quad_overdraw.shader_c",
        ];

        static void VerifyDataBlock(Resource resource, string file)
        {
            var dataBlock = resource.DataBlock;

            if (FilesWithEmptyDataBlocks.Contains(Path.GetFileName(file)))
            {
                Assert.That(dataBlock, Is.Null, file);
                return;
            }

            Assert.That(dataBlock, Is.Not.Null, file);
            Assert.That(dataBlock, Is.Not.TypeOf<UnknownDataBlock>(), file);
        }

        static void VerifyResources(Dictionary<string, Resource> resources, bool validateMissingResources = true)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput");
            var files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);
            var seenResources = new Dictionary<Resource, HashSet<BlockType>>(resources.Count);

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (name == null || !resources.TryGetValue(name, out var resource))
                {
                    if (validateMissingResources)
                    {
                        Assert.Fail($"{name}: no such resource");
                    }

                    continue;
                }

                if (!seenResources.TryGetValue(resource, out var seenBlockTypes))
                {
                    seenBlockTypes = new(resource.Blocks.Count);
                    seenResources[resource] = seenBlockTypes;
                }

                var blockName = Path.GetFileNameWithoutExtension(file);

                Enum.TryParse(blockName, false, out BlockType blockType);

                if (!resource.ContainsBlockType(blockType))
                {
                    Assert.Fail($"{name}: no such block: {blockType}");

                    continue;
                }

                seenBlockTypes.Add(blockType);

                var blockData = resource.GetBlockByType(blockType);

                if (blockData == null)
                {
                    Assert.Fail($"{name}: block is null: {blockType}");

                    continue;
                }

                var actualOutput = blockData.ToString();
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

            foreach (var resource in resources.Values)
            {
                using (resource)
                {
                    if (seenResources.TryGetValue(resource, out var seenBlockTypes))
                    {
                        foreach (var block in resource.Blocks)
                        {
                            if (!seenBlockTypes.Contains(block.Type))
                            {
                                Assert.That(block.ToString(), Is.Not.Null);
                                //Assert.Fail($"{resource.FileName}: block {block.Type} does not have a corresponding text file");
                            }
                        }

                        continue;
                    }

                    foreach (var block in resource.Blocks)
                    {
                        Assert.That(block.ToString(), Is.Not.Null);
                    }
                }
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

            Debug.Assert(ex != null);
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.Message, Does.Contain("Use ValvePak"));
        }

        [Test]
        public void ResourceDisposesStreamWhenLeaveOpenFalse()
        {
            var testFile = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "empty_data.vjs_c");
            var testData = File.ReadAllBytes(testFile);
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: false);
            Assert.That(testStream.IsDisposed, Is.False);
            Assert.That(resource.Reader, Is.Not.Null);
            resource.Dispose();
            Assert.That(testStream.IsDisposed, Is.True);
            Assert.That(resource.Reader, Is.Null);
        }

        [Test]
        public void ResourceDoesNotDisposeStreamWhenLeaveOpenTrue()
        {
            var testFile = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "empty_data.vjs_c");
            var testData = File.ReadAllBytes(testFile);
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: true);
            Assert.That(resource.Reader, Is.Not.Null);
            resource.Dispose();
            Assert.That(testStream.IsDisposed, Is.False);
            Assert.That(resource.Reader, Is.Null);
            testStream.Dispose();
            Assert.That(testStream.IsDisposed, Is.True);
        }

        [Test]
        public void ResourceDisposesFileStreamFromFilename()
        {
            var testFile = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "empty_data.vjs_c");

            var resource = new Resource();
            resource.Read(testFile);
            Assert.That(resource.Reader, Is.Not.Null);
            resource.Dispose();
            Assert.That(resource.Reader, Is.Null);
        }

        private class TestableMemoryStream : MemoryStream
        {
            public bool IsDisposed { get; private set; }

            public TestableMemoryStream(byte[] buffer) : base(buffer) { }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex SpaceRegex();
    }
}
