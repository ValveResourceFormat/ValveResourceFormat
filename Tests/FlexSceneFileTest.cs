using NUnit.Framework;
using System.IO;
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

            Assert.Multiple(() =>
            {
                Assert.That(vfe.Version, Is.EqualTo(0));
                Assert.That(vfe.FlexSettings, Has.Length.EqualTo(48));
                Assert.That(vfe.KeyNames, Has.Length.EqualTo(62));
            });
        }

        [Test]
        public void TestFlexSceneFileDecompile()
        {
            var vfeFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.vfe");
            var vfeOutputFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.txt");

            var expectedOutput = File.ReadAllText(vfeOutputFilePath);

            var vfe = new FlexSceneFile();
            vfe.Read(vfeFilePath);

            Assert.That(vfe.ToString(), Is.EqualTo(expectedOutput));
        }
    }
}
