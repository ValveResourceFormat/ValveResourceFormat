using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Resources
{
    [ExecutionPriority(TUnit.Core.Enums.Priority.High)]
    public class ResourceRoundtripTest
    {
        [Test]
        [MethodDataSource(typeof(TestFixtures), nameof(TestFixtures.CompiledFiles))]
        public async Task RoundtripSerialization(string relativePath)
        {
            var file = TestFixtures.Path(relativePath);
            var ms = new MemoryStream();

            using (var resourceOnDisk = new Resource())
            {
                try
                {
                    resourceOnDisk.Read(file);

                    // Writing z3 is not supported, re-encode those blocks with zstd
                    foreach (var binaryKV3 in resourceOnDisk.Blocks.OfType<BinaryKV3>())
                    {
                        if (binaryKV3.SerializationCompressionMethod == KV3BinaryCompressionMethod.Z3)
                        {
                            binaryKV3.SerializationCompressionMethod = KV3BinaryCompressionMethod.Zstd;
                        }
                    }

                    resourceOnDisk.Serialize(ms);
                }
                catch (NotImplementedException)
                {
                    return;
                }
            }

            ms.Position = 0;

            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(ms);

            await ResourceVerify.Parsed(resource, file);
        }
    }
}
