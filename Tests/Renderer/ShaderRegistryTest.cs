using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.Utils;

namespace Tests.Renderer
{
    [NotInParallel(nameof(ShaderRegistryTest))]
    [ExecutionPriority(TUnit.Core.Enums.Priority.AboveNormal)]
    public class ShaderRegistryTest
    {
        private string customShaderDirectory = null!;

        [Before(HookType.Test)]
        public void SetUp()
        {
            customShaderDirectory = Path.Combine(Path.GetTempPath(), "VrfCustomShaders_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(customShaderDirectory, "common"));
        }

        [After(HookType.Test)]
        public void TearDown()
        {
            ShaderRegistry.Reset();
            Directory.Delete(customShaderDirectory, recursive: true);
        }

        private Task WriteShader(string relativePath, string source)
            => File.WriteAllTextAsync(Path.Combine(customShaderDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)), source);

        private static string Preprocess(string shaderFile, ShaderLoader.ParsedShaderData? parsedData = null)
        {
            return new ShaderParser().PreprocessShader(shaderFile, parsedData ?? new ShaderLoader.ParsedShaderData());
        }

        [Test]
        public async Task CustomShaderResolvesItsOwnInclude()
        {
            await WriteShader("custom_test.vert.slang", """
                #version 460
                #include "common/custom_include.slang"
                void main() { CustomHelper(); }
                """);

            await WriteShader("common/custom_include.slang", """
                #version 460
                void CustomHelper() {}
                """);

            ShaderRegistry.AddShaderDirectory(customShaderDirectory);

            var parsedData = new ShaderLoader.ParsedShaderData();
            var source = Preprocess("custom_test.vert.slang", parsedData);

            using (Assert.Multiple())
            {
                await Assert.That(new ShaderParser().AvailableShaders).ContainsKey("custom_test");
                await Assert.That(source).Contains("void CustomHelper() {}");
                await Assert.That(source).Contains("void main() { CustomHelper(); }");
                await Assert.That(parsedData.SourceFiles).Contains("common/custom_include.slang");
            }
        }

        [Test]
        public async Task MountedIncludeOverridesBuiltinOne()
        {
            await Assert.That(Preprocess("complex.vert.slang")).DoesNotContain("CustomUtilsMarker");

            // complex.vert.slang includes common/utils.slang
            await WriteShader("common/utils.slang", """
                #version 460
                void CustomUtilsMarker() {}
                """);

            ShaderRegistry.AddShaderDirectory(customShaderDirectory);

            await Assert.That(Preprocess("complex.vert.slang")).Contains("void CustomUtilsMarker() {}");
        }

        [Test]
        public async Task LastAddedShaderDirectoryHasPriority()
        {
            var secondDirectory = customShaderDirectory + "_second";
            Directory.CreateDirectory(secondDirectory);

            try
            {
                await WriteShader("complex.vert.slang", "#version 460\nvoid main() { First(); }");
                await File.WriteAllTextAsync(Path.Combine(secondDirectory, "complex.vert.slang"), "#version 460\nvoid main() { Second(); }");

                ShaderRegistry.AddShaderDirectory(customShaderDirectory);
                ShaderRegistry.AddShaderDirectory(secondDirectory);

                await Assert.That(Preprocess("complex.vert.slang")).Contains("void main() { Second(); }");
            }
            finally
            {
                Directory.Delete(secondDirectory, recursive: true);
            }
        }

        [Test]
        public async Task PreprocessedSourceCanBePatched()
        {
            // Shaders can also be customized by patching the preprocessed source instead of overriding whole files.
            // Note the result cannot be written back out as a shader file, as it is already fully inlined.
            var parsedData = new ShaderLoader.ParsedShaderData();
            var patched = Preprocess("complex.vert.slang", parsedData)
                .Replace("void main()", "void PatchedMain()", StringComparison.Ordinal);

            using (Assert.Multiple())
            {
                await Assert.That(patched).Contains("void PatchedMain()");
                await Assert.That(patched).DoesNotContain("void main()");
                await Assert.That(parsedData.Sources).IsEmpty().Because("Sources are only filled in by ShaderLoader when it compiles a shader");
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
        public async Task CustomShaderAndVertexStructAgreeOnLocations()
        {
            await WriteShader("custom_vertex.vert.slang", """
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

            using (Assert.Multiple())
            {
                // Mesh attributes keep their canonical slot
                await Assert.That(source).Contains($"layout (location = {(int)VertexSlot.Position}) in vec3 vPOSITION;");
                await Assert.That(source).Contains($"layout (location = {(int)VertexSlot.TexCoord}) in vec2 vTEXCOORD;");

                // A declaration that places itself is left alone, not stamped a second time
                await Assert.That(source).Contains("layout (location = 12) in float vAttr2;");
                await Assert.That(source).DoesNotContain(") layout (");

                // The custom ones fill the slots left free, in name order rather than declaration order, and
                // the pinned slot is out of that pool even though it is declared between them
                await Assert.That(source).Contains($"layout (location = {(int)VertexSlot.BlendIndices}) in float vAttr1;");
                await Assert.That(source).Contains($"layout (location = {(int)VertexSlot.BlendWeight}) in float vAttr3;");

                // The struct declares the same names and reaches the same locations, which is what lets its
                // vertex array object feed this shader. A custom attribute carries the semantic of the slot it
                // landed on, which is how the mesh path resolves it back.
                await Assert.That(SemanticOf(fields, "vAttr1")).IsEqualTo(VertexAttributeLocations.GetSemantic((int)VertexSlot.BlendIndices));
                await Assert.That(SemanticOf(fields, "vAttr3")).IsEqualTo(VertexAttributeLocations.GetSemantic((int)VertexSlot.BlendWeight));
                await Assert.That(SemanticOf(fields, "vAttr2")).IsEqualTo(VertexAttributeLocations.GetSemantic(12));
            }
        }

        [Test]
        public async Task OnlyVfxShaderNamesFallBackToComplex()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx")).IsEqualTo("complex");
                await Assert.That(ShaderLoader.GetShaderFileByName("sky.vfx")).IsEqualTo("sky");

                // Renderer shader files are loaded as themselves, and throw when they do not exist
                await Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass")).IsEqualTo("mygame_glass");
                await Assert.That(ShaderLoader.GetShaderFileByName("vrf.grid")).IsEqualTo("grid");
            }
        }

        [Test]
        public async Task ShaderMappingsOverrideBuiltinOnes()
        {
            await Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx")).IsEqualTo("complex");

            ShaderRegistry.AddShaderMapping("mygame_glass.vfx", "mygame_glass");
            ShaderRegistry.AddShaderMapping("sky.vfx", "mygame_sky");

            using (Assert.Multiple())
            {
                await Assert.That(ShaderLoader.GetShaderFileByName("mygame_glass.vfx")).IsEqualTo("mygame_glass");
                await Assert.That(ShaderLoader.GetShaderFileByName("sky.vfx")).IsEqualTo("mygame_sky");
            }
        }
    }
}
