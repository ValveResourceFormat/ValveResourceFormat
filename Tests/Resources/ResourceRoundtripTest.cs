using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;

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

            await ResourceVerify.Parsed(resource, file);
        }
    }
}
