using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests
{
    public partial class Test
    {
        // TODO: Add asserts for blocks/resources that were skipped
        [Test]
        public async Task ReadBlocks()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.TestDirectory!, "Files");
            var files = Directory.GetFiles(path, "*.*_c", new EnumerationOptions
            {
                RecurseSubdirectories = true,
            });

            if (files.Length == 0)
            {
                Fail.Test("There are no files to test.");
            }

            foreach (var file in files)
            {
                var resource = new Resource
                {
                    FileName = file,
                };
                resource.Read(file);

                resources.Add(Path.GetFileName(file), resource);

                await Assert.That(resource.ResourceType).IsNotEqualTo(ResourceType.Unknown);
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(file.AsSpan())));

                // Verify extension
                var extension = Path.GetExtension(file);

                if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    extension = extension[..^2];
                }

                var attribute = "." + resource.ResourceType.GetExtension();
                await Assert.That(attribute).IsEqualTo(extension).Because(file);

                if (resource.ResourceType != ResourceType.Map) /// Tested by <see cref="MapExtractTest"/>
                {
                    InternalTestExtraction.Test(resource);
                }
            }

            await VerifyResources(resources);
        }

        [Test]
        public async Task RoundtripSerialization()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.TestDirectory!, "Files");
            var files = Directory.GetFiles(path, "*.*_c", new EnumerationOptions
            {
                RecurseSubdirectories = true,
            });
            var total = 0;
            var notImplemented = 0;

            if (files.Length == 0)
            {
                Fail.Test("There are no files to test.");
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
                var resource = new Resource
                {
                    FileName = file,
                };
                resource.Read(ms);

                resources.Add(Path.GetFileName(file), resource);

                await Assert.That(resource.ResourceType).IsNotEqualTo(ResourceType.Unknown);

                // Verify extension
                var extension = Path.GetExtension(file);

                if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    extension = extension[..^2];
                }

                var attribute = "." + resource.ResourceType.GetExtension();
                await Assert.That(attribute).IsEqualTo(extension).Because(file);

                if (resource.ResourceType != ResourceType.Map) /// Tested by <see cref="MapExtractTest"/>
                {
                    InternalTestExtraction.Test(resource);
                }
            }

            await VerifyResources(resources, validateMissingResources: false);

            await Console.Out.WriteLineAsync($"{notImplemented} out of {total} files are not yet serializable.");
        }

        [Test]
        public async Task ReadBlocksWithMemoryStream()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.TestDirectory!, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Fail.Test("There are no files to test.");
            }

            foreach (var file in files)
            {
                using var resource = new Resource
                {
                    FileName = file,
                };

                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                var ms = new MemoryStream();
                await fs.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);

                resource.Read(ms);

                await VerifyDataBlock(resource, file);
            }
        }

        [Test]
        public async Task ReadBlocksNoFileName()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.TestDirectory!, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Fail.Test("There are no files to test.");
            }

            foreach (var file in files)
            {
                using var resource = new Resource();
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                resource.Read(fs);

                await VerifyDataBlock(resource, file);
            }
        }

        private static readonly HashSet<string> FilesWithEmptyDataBlocks =
        [
            "dota.vmap_c",
            "empty_data.vjs_c",
            "sbox_visualize_quad_overdraw.shader_c",
        ];

        static async Task VerifyDataBlock(Resource resource, string file)
        {
            var dataBlock = resource.DataBlock;

            if (FilesWithEmptyDataBlocks.Contains(Path.GetFileName(file)))
            {
                await Assert.That(dataBlock).IsNull().Because(file);
                return;
            }

            await Assert.That(dataBlock).IsNotNull().Because(file);
            await Assert.That(dataBlock).IsNotTypeOf<UnknownDataBlock>().Because(file);
        }

        // Set VRF_REGEN_FIXTURES=1 to rewrite mismatching ValidOutput dumps in the source tree
        private static readonly bool RegenerateFixtures = Environment.GetEnvironmentVariable("VRF_REGEN_FIXTURES") == "1";

        private static string GetSourceValidOutputPath([CallerFilePath] string sourceFile = "")
            => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Files", "ValidOutput");

        static async Task VerifyResources(Dictionary<string, Resource> resources, bool validateMissingResources = true)
        {
            var path = Path.Combine(TestContext.TestDirectory!, "Files", "ValidOutput");
            var files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);
            var seenResources = new Dictionary<Resource, HashSet<BlockType>>(resources.Count);

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (name == null || !resources.TryGetValue(name, out var resource))
                {
                    if (validateMissingResources)
                    {
                        Fail.Test($"{name}: no such resource");
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
                    Fail.Test($"{name}: no such block: {blockType}");

                    continue;
                }

                seenBlockTypes.Add(blockType);

                var blockData = resource.GetBlockByType(blockType);

                if (blockData == null)
                {
                    Fail.Test($"{name}: block is null: {blockType}");

                    continue;
                }

                var rawOutput = blockData.ToString();
                var expectedOutput = await File.ReadAllTextAsync(file);

                // We don't care about Valve's messy whitespace, so just strip it.
                var actualOutput = SpaceRegex().Replace(rawOutput, string.Empty);

                expectedOutput = expectedOutput.Replace("Source 2 Viewer - https://valveresourceformat.github.io", StringToken.VRF_GENERATOR, StringComparison.Ordinal);
                expectedOutput = SpaceRegex().Replace(expectedOutput, string.Empty);

                //Assert.That(actualOutput, Is.EqualTo(expectedOutput));
                if (expectedOutput != actualOutput)
                {
                    if (RegenerateFixtures)
                    {
                        // Fixtures are stored with the version-free generator string
                        var sourceFile = Path.Combine(GetSourceValidOutputPath(), Path.GetRelativePath(path, file));
                        await File.WriteAllTextAsync(sourceFile, rawOutput.Replace(StringToken.VRF_GENERATOR, "Source 2 Viewer - https://valveresourceformat.github.io", StringComparison.Ordinal));
                        await Console.Error.WriteLineAsync($"Regenerated '{sourceFile}'");
                    }
                    else
                    {
                        await Console.Error.WriteLineAsync($"File '{file}' has mismatching ToString() in {blockType}");
                    }
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
                                await Assert.That(block.ToString()).IsNotNull();
                                //Assert.Fail($"{resource.FileName}: block {block.Type} does not have a corresponding text file");
                            }
                        }

                        continue;
                    }

                    foreach (var block in resource.Blocks)
                    {
                        await Assert.That(block.ToString()).IsNotNull();
                    }
                }
            }
        }

        [Test]
        public void InvalidResourceThrows()
        {
            using var resource = new Resource();
            using var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray());

            Assert.ThrowsExactly<UnexpectedMagicException>(() => resource.Read(ms));
        }

        [Test]
        public async Task PackageInResourceThrows()
        {
            var data = new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x00, 0x00 };

            using var resource = new Resource();
            using var ms = new MemoryStream(data);

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => resource.Read(ms));

            Debug.Assert(ex != null);
            await Assert.That(ex).IsNotNull();
            await Assert.That(ex.Message).Contains("Use ValvePak");
        }

        [Test]
        public async Task ResourceDisposesStreamWhenLeaveOpenFalse()
        {
            var testFile = Path.Combine(TestContext.TestDirectory!, "Files", "empty_data.vjs_c");
            var testData = await File.ReadAllBytesAsync(testFile);
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: false);
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsFalse();
                await Assert.That(resource.Reader).IsNotNull();
            }
            resource.Dispose();
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsTrue();
                await Assert.That(resource.Reader).IsNull();
            }
        }

        [Test]
        public async Task ResourceDoesNotDisposeStreamWhenLeaveOpenTrue()
        {
            var testFile = Path.Combine(TestContext.TestDirectory!, "Files", "empty_data.vjs_c");
            var testData = await File.ReadAllBytesAsync(testFile);
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: true);
            await Assert.That(resource.Reader).IsNotNull();
            resource.Dispose();
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsFalse();
                await Assert.That(resource.Reader).IsNull();
            }
            await testStream.DisposeAsync();
            await Assert.That(testStream.IsDisposed).IsTrue();
        }

        [Test]
        public async Task ResourceDisposesFileStreamFromFilename()
        {
            var testFile = Path.Combine(TestContext.TestDirectory!, "Files", "empty_data.vjs_c");

            var resource = new Resource();
            resource.Read(testFile);
            await Assert.That(resource.Reader).IsNotNull();
            resource.Dispose();
            await Assert.That(resource.Reader).IsNull();
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
