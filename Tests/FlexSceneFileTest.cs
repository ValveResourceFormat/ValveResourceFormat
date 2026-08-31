using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.FlexSceneFile;
using ValveResourceFormat.IO;

namespace Tests
{
    public class FlexSceneFileTest
    {
        [Test]
        public async Task ExtractProducesTextContentFile()
        {
            var vfeFilePath = Path.Combine(TestContext.TestDirectory!, "Files", "phonemes.vfe");

            await using var stream = File.OpenRead(vfeFilePath);
            var extract = new FlexSceneExtract(stream);
            using var contentFile = extract.ToContentFile();

            var expected = await File.ReadAllTextAsync(Path.Combine(TestContext.TestDirectory!, "Files", "phonemes.txt"));

            using (Assert.Multiple())
            {
                await Assert.That(contentFile.FileName).IsEqualTo("phonemes.txt");
                await Assert.That(Encoding.UTF8.GetString(contentFile.Data!).ReplaceLineEndings()).IsEqualTo(expected.ReplaceLineEndings());
            }
        }

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
