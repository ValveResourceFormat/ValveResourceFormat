using NUnit.Framework;
using System.IO;
using ValveResourceFormat.FaceExpressionData;

namespace Tests
{
    public class FaceExpressionTest
    {
        [Test]
        public void TestFaceExpression()
        {
            var vfeFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.vfe");
            var vfe = new FaceExpressionData();
            vfe.Read(vfeFilePath);

            Assert.That(vfe.Version, Is.EqualTo(0));
            Assert.That(vfe.FlexSettings, Has.Length.EqualTo(48));
            Assert.That(vfe.KeyNames, Has.Length.EqualTo(62));
        }

        [Test]
        public void TestFaceExpressionDecompile()
        {
            var vfeFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "phonemes.vfe");
            var vfeOutputFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput", "phonemes.txt");

            var expectedOutput = File.ReadAllText(vfeOutputFilePath);

            var vfe = new FaceExpressionData();
            vfe.Read(vfeFilePath);

            Assert.That(vfe.ToString(), Is.EqualTo(expectedOutput));
        }
    }
}
