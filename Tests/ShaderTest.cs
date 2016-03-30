using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

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
