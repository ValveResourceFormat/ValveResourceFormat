using System.IO;
using NUnit.Framework;
using ValveResourceFormat.FlexSceneFile;

namespace Tests
{
    public class FlexSceneFileTest
    {
        [Test]
        public void TestFlexSceneFile()
        {
            var vfeFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.vfe");
            var vfe = new FlexSceneFile();
            vfe.Read(vfeFilePath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vfe.Version, Is.Zero);
                Assert.That(vfe.FlexSettings, Has.Length.EqualTo(48));
                Assert.That(vfe.KeyNames, Has.Length.EqualTo(62));
            }
        }

        [Test]
        public void TestFlexSceneFileDecompile()
        {
            var vfeFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.vfe");
            var vfeOutputFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.txt");

            var expectedOutput = File.ReadAllText(vfeOutputFilePath).ReplaceLineEndings();

            var vfe = new FlexSceneFile();
            vfe.Read(vfeFilePath);

            var actualOutput = vfe.ToString().ReplaceLineEndings();

            Assert.That(actualOutput, Is.EqualTo(expectedOutput));
        }
    }
}
