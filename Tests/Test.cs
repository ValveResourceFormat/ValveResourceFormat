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
    [ExecutionPriority(TUnit.Core.Enums.Priority.High)]
    public partial class Test
    {
        private static string FilesDirectory => Path.Combine(TestContext.TestDirectory!, "Files");

        public static IEnumerable<string> CompiledFiles() => EnumerateCompiledFiles(recursive: true);

        public static IEnumerable<string> TopLevelCompiledFiles() => EnumerateCompiledFiles(recursive: false);

        private static List<string> EnumerateCompiledFiles(bool recursive)
        {
            var files = Directory.GetFiles(FilesDirectory, "*.*_c", new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
            });

            if (files.Length == 0)
            {
                throw new InvalidOperationException($"There are no files to test in {FilesDirectory}.");
            }

            return [.. files.Select(file => Path.GetRelativePath(FilesDirectory, file))];
        }

        // TODO: Add asserts for blocks/resources that were skipped
        [Test]
        [MethodDataSource(nameof(CompiledFiles))]
        public async Task ReadBlocks(string relativePath)
        {
            var file = Path.Combine(FilesDirectory, relativePath);
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            await Assert.That(resource.ResourceType).IsNotEqualTo(ResourceType.Unknown);
            await Assert.That(resource.ResourceType).IsEqualTo(ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(file.AsSpan())));

            await VerifyExtension(resource, file);

            if (resource.ResourceType != ResourceType.Map) /// Tested by <see cref="MapExtractTest"/>
            {
                InternalTestExtraction.Test(resource);
            }

            await VerifyResourceOutput(resource, Path.GetFileName(file));
        }

        [Test]
        [MethodDataSource(nameof(CompiledFiles))]
        public async Task RoundtripSerialization(string relativePath)
        {
            var file = Path.Combine(FilesDirectory, relativePath);
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
                    return;
                }

                try
                {
                    resourceOnDisk.Serialize(ms);
                }
                catch (NotImplementedException)
                {
                    return;
                }
            }

            ms.Position = 0;

            // Now try to parse what we just wrote
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(ms);

            await Assert.That(resource.ResourceType).IsNotEqualTo(ResourceType.Unknown);

            await VerifyExtension(resource, file);

            if (resource.ResourceType != ResourceType.Map) /// Tested by <see cref="MapExtractTest"/>
            {
                InternalTestExtraction.Test(resource);
            }

            await VerifyResourceOutput(resource, Path.GetFileName(file));
        }

        [Test]
        [MethodDataSource(nameof(TopLevelCompiledFiles))]
        public async Task ReadBlocksWithMemoryStream(string relativePath)
        {
            var file = Path.Combine(FilesDirectory, relativePath);
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

        [Test]
        [MethodDataSource(nameof(TopLevelCompiledFiles))]
        public async Task ReadBlocksNoFileName(string relativePath)
        {
            var file = Path.Combine(FilesDirectory, relativePath);
            using var resource = new Resource();
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            resource.Read(fs);

            await VerifyDataBlock(resource, file);
        }

        [Test]
        public async Task ValidOutputHasNoOrphanedFolders()
        {
            var fixtures = CompiledFiles().Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

            using (Assert.Multiple())
            {
                foreach (var directory in Directory.GetDirectories(ValidOutputDirectory))
                {
                    await Assert.That(fixtures).Contains(Path.GetFileName(directory)).Because($"{Path.GetFileName(directory)}: no such resource");
                }
            }
        }

        private static async Task VerifyExtension(Resource resource, string file)
        {
            var extension = Path.GetExtension(file);

            if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                extension = extension[..^2];
            }

            var attribute = "." + resource.ResourceType.GetExtension();
            await Assert.That(attribute).IsEqualTo(extension).Because(file);
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

        private static string ValidOutputDirectory => Path.Combine(FilesDirectory, "ValidOutput");

        static async Task VerifyResourceOutput(Resource resource, string name)
        {
            var directory = Path.Combine(ValidOutputDirectory, name);
            var seenBlockTypes = new HashSet<BlockType>(resource.Blocks.Count);

            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory, "*.*txt"))
                {
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

                    //await Assert.That(actualOutput).IsEqualTo(expectedOutput);
                    if (expectedOutput != actualOutput)
                    {
                        if (RegenerateFixtures)
                        {
                            // Fixtures are stored with the version-free generator string
                            var sourceFile = Path.Combine(GetSourceValidOutputPath(), Path.GetRelativePath(ValidOutputDirectory, file));
                            await File.WriteAllTextAsync(sourceFile, rawOutput.Replace(StringToken.VRF_GENERATOR, "Source 2 Viewer - https://valveresourceformat.github.io", StringComparison.Ordinal));
                            await Console.Error.WriteLineAsync($"Regenerated '{sourceFile}'");
                        }
                        else
                        {
                            await Console.Error.WriteLineAsync($"File '{file}' has mismatching ToString() in {blockType}");
                        }
                    }
                }
            }

            foreach (var block in resource.Blocks)
            {
                if (!seenBlockTypes.Contains(block.Type))
                {
                    await Assert.That(block.ToString()).IsNotNull();
                    //Fail.Test($"{resource.FileName}: block {block.Type} does not have a corresponding text file");
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
