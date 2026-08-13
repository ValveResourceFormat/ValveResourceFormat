using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.Utils;

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


        // Field order deliberately differs from the shader's declaration order, since neither decides a location
        [StructLayout(LayoutKind.Sequential)]
        private struct TestVertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;
            [VertexAttribute("vAttr1")] public float Attribute1;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector2 TexCoord;
            [VertexAttribute("vAttr2", location: 12)] public float Attribute2;
            [VertexAttribute("vAttr3")] public float Attribute3;
        }

        private static (string Name, int Index) SemanticOf(VBIB.RenderInputLayoutField[] fields, string shaderInput)
        {
            var field = Array.Find(fields, field => field.ShaderSemantic == shaderInput);
            return (field.SemanticName, field.SemanticIndex);
        }

        [Test]
        public void CustomShaderAndVertexStructAgreeOnLocations()
        {
            WriteShader("custom_vertex.vert.slang", """
                #version 460
                in vec3 vPOSITION;
                in float vAttr1;
                in vec2 vTEXCOORD;
                layout (location = 12) in float vAttr2;
                in float vAttr3;
                void main() { gl_Position = vec4(vPOSITION, vAttr1 + vAttr2 + vAttr3 + vTEXCOORD.x); }
                """);

            ShaderRegistry.AddShaderDirectory(customShaderDirectory);

            var source = Preprocess("custom_vertex.vert.slang");
            var fields = VertexInputLayout.FromStruct<TestVertex>().Fields();

            using (Assert.EnterMultipleScope())
            {
                // Mesh attributes keep their canonical slot
                Assert.That(source, Does.Contain($"layout (location = {(int)VertexSlot.Position}) in vec3 vPOSITION;"));
                Assert.That(source, Does.Contain($"layout (location = {(int)VertexSlot.TexCoord}) in vec2 vTEXCOORD;"));

                // A declaration that places itself is left alone, not stamped a second time
                Assert.That(source, Does.Contain("layout (location = 12) in float vAttr2;"));
                Assert.That(source, Does.Not.Contain(") layout ("));

                // The custom ones fill the slots left free, in name order rather than declaration order, and
                // the pinned slot is out of that pool even though it is declared between them
                Assert.That(source, Does.Contain($"layout (location = {(int)VertexSlot.BlendIndices}) in float vAttr1;"));
                Assert.That(source, Does.Contain($"layout (location = {(int)VertexSlot.BlendWeight}) in float vAttr3;"));

                // The struct declares the same names and reaches the same locations, which is what lets its
                // vertex array object feed this shader. A custom attribute carries the semantic of the slot it
                // landed on, which is how the mesh path resolves it back.
                Assert.That(SemanticOf(fields, "vAttr1"), Is.EqualTo(VertexAttributeLocations.GetSemantic((int)VertexSlot.BlendIndices)));
                Assert.That(SemanticOf(fields, "vAttr3"), Is.EqualTo(VertexAttributeLocations.GetSemantic((int)VertexSlot.BlendWeight)));
                Assert.That(SemanticOf(fields, "vAttr2"), Is.EqualTo(VertexAttributeLocations.GetSemantic(12)));
            }
        }

        [Test]
        public void OnlyVfxShaderNamesFallBackToComplex()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx"), Is.EqualTo("complex"));
                Assert.That(ShaderLoader.GetShaderFileByName("sky.vfx"), Is.EqualTo("sky"));

                // Renderer shader files are loaded as themselves, and throw when they do not exist
                Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass"), Is.EqualTo("mygame_glass"));
                Assert.That(ShaderLoader.GetShaderFileByName("vrf.grid"), Is.EqualTo("grid"));
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
