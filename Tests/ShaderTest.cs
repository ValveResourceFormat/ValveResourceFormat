using System.IO;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    public class ShaderTest
    {
        [Test]
        public void Test()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Shaders");
            var files = Directory.GetFiles(path, "*.vcs");

            foreach (var file in files)
            {
                var shader = new CompiledShader();
                shader.Read(file);
            }
        }
    }
}
