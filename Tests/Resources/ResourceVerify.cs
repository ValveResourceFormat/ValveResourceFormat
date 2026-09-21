using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests.Resources
{
    /// <summary>
    /// The checks that every fixture has to pass, no matter which way the resource was read.
    /// </summary>
    internal static class ResourceVerify
    {
        private static readonly HashSet<string> FilesWithEmptyDataBlocks =
        [
            "dota.vmap_c",
            "empty_data.vjs_c",
            "sbox_visualize_quad_overdraw.shader_c",
        ];

        /// <summary>Verifies a fully parsed resource, and the text it dumps for each of its blocks.</summary>
        public static async Task Parsed(Resource resource, string file)
        {
            await Assert.That(resource.ResourceType).IsNotEqualTo(ResourceType.Unknown);

            await Extension(resource, file);

            if (resource.ResourceType != ResourceType.Map) // Tested by MapExtractTest
            {
                InternalTestExtraction.Test(resource);
            }

            await ValidOutput.Verify(resource, Path.GetFileName(file));
        }

        private static async Task Extension(Resource resource, string file)
        {
            var extension = Path.GetExtension(file);

            if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                extension = extension[..^2];
            }

            var attribute = "." + resource.ResourceType.GetExtension();
            await Assert.That(attribute).IsEqualTo(extension).Because(file);
        }

        public static async Task DataBlock(Resource resource, string file)
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
    }
}
