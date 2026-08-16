using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class PostProcessingTest
    {
        [Test]
        public async Task TestPostProcessing()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "a1_intro_world_courtyard.vpost_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var postProcessing = (PostProcessing?)resource.DataBlock;

            Debug.Assert(postProcessing != null);
            await Assert.That(postProcessing.ToValvePostProcessing()).IsNotEmpty();
        }
    }
}
