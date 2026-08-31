using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Smd;

namespace Tests
{
    public class SmdExportTest
    {
        [Test]
        public async Task SkeletonSmdExportWorks()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "chicken.vnmskel_c"));

            var skelExtract = new NmSkeletonExtract(resource);
            var smdData = skelExtract.ToSmdData();

            await Assert.That(smdData).IsNotNull();
            await Assert.That(smdData.Bones.Count).IsGreaterThan(0);
            await Assert.That(smdData.Type).IsEqualTo(SmdType.Skeleton);

            var smdBytes = smdData.ToBytes();
            await Assert.That(smdBytes).IsNotNull();
            await Assert.That(smdBytes.Length).IsGreaterThan(0);

            var smdText = System.Text.Encoding.UTF8.GetString(smdBytes);
            await Assert.That(smdText).Contains("version 1");
            await Assert.That(smdText).Contains("nodes");
            await Assert.That(smdText).Contains("skeleton");
            await Assert.That(smdText).Contains("time 0");
            await Assert.That(smdText).Contains("end");
        }
    }
}
