using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.FlexSceneFile;

namespace Tests
{
    public class FlexSceneFileTest
    {
        [Test]
        public async Task TestFlexSceneFile()
        {
            var vfeFilePath = Path.Combine(TestContext.TestDirectory!, "Files", "phonemes.vfe");
            var vfe = new FlexSceneFile();
            vfe.Read(vfeFilePath);

            using (Assert.Multiple())
            {
                await Assert.That(vfe.Version).IsZero();
                await Assert.That(vfe.FlexSettings).Count().IsEqualTo(48);
                await Assert.That(vfe.KeyNames).Count().IsEqualTo(62);
            }
        }

        [Test]
        public async Task TestFlexSceneFileDecompile()
        {
            var vfeFilePath = Path.Combine(TestContext.TestDirectory!, "Files", "phonemes.vfe");
            var vfeOutputFilePath = Path.Combine(TestContext.TestDirectory!, "Files", "phonemes.txt");

            var expectedOutput = (await File.ReadAllTextAsync(vfeOutputFilePath)).ReplaceLineEndings();

            var vfe = new FlexSceneFile();
            vfe.Read(vfeFilePath);

            var actualOutput = vfe.ToString().ReplaceLineEndings();

            await Assert.That(actualOutput).IsEqualTo(expectedOutput);
        }
    }
}
