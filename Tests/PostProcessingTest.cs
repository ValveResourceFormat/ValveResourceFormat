using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class PostProcessingTest
    {
        [Test]
        public void TestPostProcessing()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "a1_intro_world_courtyard.vpost_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var postProcessing = (PostProcessing?)resource.DataBlock;

            Debug.Assert(postProcessing != null);
            Assert.That(postProcessing.ToValvePostProcessing(), Is.Not.Empty);
        }
    }
}
