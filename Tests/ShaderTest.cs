using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat.CompiledShader;

namespace Tests
{
    public class ShaderTest
    {
        [Test]
        public void ParseShaders()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Shaders");
            var files = Directory.GetFiles(path, "*.vcs");

            foreach (var file in files)
            {
                var shader = new ShaderCollection();

                using var sw = new StringWriter(CultureInfo.InvariantCulture);
                var originalOutput = Console.Out;
                Console.SetOut(sw);

                shader.Read(file);

                Console.SetOut(originalOutput);
            }
        }
    }
}
