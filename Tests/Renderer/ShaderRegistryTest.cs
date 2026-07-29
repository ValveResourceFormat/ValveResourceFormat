using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat.Renderer.Shaders;

namespace Tests.Renderer
{
    public class ShaderRegistryTest
    {
        private string customShaderDirectory;

        [SetUp]
        public void SetUp()
        {
            customShaderDirectory = Path.Combine(Path.GetTempPath(), "VrfCustomShaders_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(customShaderDirectory, "common"));
        }

        [TearDown]
        public void TearDown()
        {
            ShaderRegistry.Reset();
            Directory.Delete(customShaderDirectory, recursive: true);
        }

        private void WriteShader(string relativePath, string source)
        {
            File.WriteAllText(Path.Combine(customShaderDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)), source);
        }

        private static string Preprocess(string shaderFile, ShaderLoader.ParsedShaderData? parsedData = null)
        {
            return new ShaderParser().PreprocessShader(shaderFile, parsedData ?? new ShaderLoader.ParsedShaderData());
        }

        [Test]
        public void CustomShaderResolvesItsOwnInclude()
        {
            WriteShader("custom_test.vert.slang", """
                #version 460
                #include "common/custom_include.slang"
                void main() { CustomHelper(); }
                """);

            WriteShader("common/custom_include.slang", """
                #version 460
                void CustomHelper() {}
                """);

            ShaderRegistry.AddShaderDirectory(customShaderDirectory);

            var parsedData = new ShaderLoader.ParsedShaderData();
            var source = Preprocess("custom_test.vert.slang", parsedData);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(new ShaderParser().AvailableShaders, Contains.Key("custom_test"));
                Assert.That(source, Does.Contain("void CustomHelper() {}"));
                Assert.That(source, Does.Contain("void main() { CustomHelper(); }"));
                Assert.That(parsedData.SourceFiles, Does.Contain("common/custom_include.slang"));
            }
        }

        [Test]
        public void MountedIncludeOverridesBuiltinOne()
        {
            Assert.That(Preprocess("complex.vert.slang"), Does.Not.Contain("CustomUtilsMarker"));

            // complex.vert.slang includes common/utils.slang
            WriteShader("common/utils.slang", """
                #version 460
                void CustomUtilsMarker() {}
                """);

            ShaderRegistry.AddShaderDirectory(customShaderDirectory);

            Assert.That(Preprocess("complex.vert.slang"), Does.Contain("void CustomUtilsMarker() {}"));
        }

        [Test]
        public void LastAddedShaderDirectoryHasPriority()
        {
            var secondDirectory = customShaderDirectory + "_second";
            Directory.CreateDirectory(secondDirectory);

            try
            {
                WriteShader("complex.vert.slang", "#version 460\nvoid main() { First(); }");
                File.WriteAllText(Path.Combine(secondDirectory, "complex.vert.slang"), "#version 460\nvoid main() { Second(); }");

                ShaderRegistry.AddShaderDirectory(customShaderDirectory);
                ShaderRegistry.AddShaderDirectory(secondDirectory);

                Assert.That(Preprocess("complex.vert.slang"), Does.Contain("void main() { Second(); }"));
            }
            finally
            {
                Directory.Delete(secondDirectory, recursive: true);
            }
        }

        [Test]
        public void PreprocessedSourceCanBePatched()
        {
            // Shaders can also be customized by patching the preprocessed source instead of overriding whole files.
            // Note the result cannot be written back out as a shader file, as it is already fully inlined.
            var parsedData = new ShaderLoader.ParsedShaderData();
            var patched = Preprocess("complex.vert.slang", parsedData)
                .Replace("void main()", "void PatchedMain()", StringComparison.Ordinal);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(patched, Does.Contain("void PatchedMain()"));
                Assert.That(patched, Does.Not.Contain("void main()"));
                Assert.That(parsedData.Sources, Is.Empty, "Sources are only filled in by ShaderLoader when it compiles a shader");
            }
        }

        [Test]
        public void ShaderMappingsOverrideBuiltinOnes()
        {
            Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx"), Is.EqualTo("complex"));

            ShaderRegistry.AddShaderMapping("mygame_glass.vfx", "mygame_glass");
            ShaderRegistry.AddShaderMapping("sky.vfx", "mygame_sky");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx"), Is.EqualTo("mygame_glass"));
                Assert.That(ShaderLoader.GetShaderFileByName("sky.vfx"), Is.EqualTo("mygame_sky"));
            }
        }
    }
}
