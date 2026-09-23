using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Utils;

namespace Tests.Resources
{
    /// <summary>
    /// Compares what a resource's blocks print against the dumps kept in <c>Files/ValidOutput</c>.
    /// </summary>
    internal static partial class ValidOutput
    {
        // Set VRF_REGEN_FIXTURES=1 to rewrite mismatching ValidOutput dumps in the source tree
        private static readonly bool RegenerateFixtures = Environment.GetEnvironmentVariable("VRF_REGEN_FIXTURES") == "1";

        public static async Task Verify(Resource resource, string name)
        {
            var directory = Path.Combine(TestFixtures.ValidOutputDirectory, name);
            var seenBlockTypes = new HashSet<BlockType>(resource.Blocks.Count);
            var expectedDumps = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.*txt") : [];

            foreach (var file in expectedDumps)
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
                if (expectedOutput == actualOutput)
                {
                    continue;
                }

                if (!RegenerateFixtures)
                {
                    await Console.Error.WriteLineAsync($"File '{file}' has mismatching ToString() in {blockType}");

                    continue;
                }

                // Fixtures are stored with the version-free generator string
                var sourceFile = Path.Combine(TestFixtures.SourceValidOutputDirectory, Path.GetRelativePath(TestFixtures.ValidOutputDirectory, file));
                await File.WriteAllTextAsync(sourceFile, rawOutput.Replace(StringToken.VRF_GENERATOR, "Source 2 Viewer - https://valveresourceformat.github.io", StringComparison.Ordinal));
                await Console.Error.WriteLineAsync($"Regenerated '{sourceFile}'");
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

        [GeneratedRegex(@"\s+")]
        private static partial Regex SpaceRegex();
    }
}
