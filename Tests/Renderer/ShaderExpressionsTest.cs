using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Serialization.VfxEval;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    public class ShaderExpressionsTest
    {
        private sealed class NoParams : IVfxEvalContext
        {
            public bool TryGetMaterialParam(StringToken name, out Vector4 value)
            {
                value = default;
                return false;
            }
        }

        private static ShaderLoader.ParsedShaderData Compile(params string[] blocks)
        {
            var parsed = new ShaderLoader.ParsedShaderData();
            parsed.Uniforms.Add("g_flA");
            parsed.Uniforms.Add("g_flB");

            foreach (var source in blocks)
            {
                parsed.ExpressionBlocks.Add(new(source, "test.slang", 10));
            }

            ShaderParser.CompileExpressions(parsed);
            return parsed;
        }

        private static float Evaluate(ShaderLoader.ParsedShaderData parsed, string name)
        {
            VfxEvalRuntime.TryEvaluate(parsed.Expressions[name], new NoParams(), out var value, out _);
            return value.X;
        }

        [Test]
        public async Task TestLocalsAreReadAtTheirLatestAssignment()
        {
            var parsed = Compile("x = 2;\ny = x + 1;\ng_flA = x * y;\nx = 5;\ng_flB = x\n    - 1;\n");

            await Assert.That(Evaluate(parsed, "g_flA")).IsEqualTo(6f);
            await Assert.That(Evaluate(parsed, "g_flB")).IsEqualTo(4f);
        }

        [Test]
        public async Task TestFunctionsAreSharedAcrossBlocks()
        {
            var parsed = Compile(
                "g_flA = Twice(3);\n",
                "// Doubles a value\nfloat Twice(float x)\n{\n    float y = x * 2;\n    return y;\n}\n\ng_flB = Twice(2) + 1;\n");

            await Assert.That(Evaluate(parsed, "g_flA")).IsEqualTo(6f);
            await Assert.That(Evaluate(parsed, "g_flB")).IsEqualTo(5f);
        }

        [Test]
        public async Task TestUniformsDeclareInputsWithDefaults()
        {
            var parsed = Compile("uniform vec2 g_vScale = vec2(2.0);\nuniform float g_flRotation;\ng_flA = g_vScale.x;\n");

            await Assert.That(parsed.ExpressionInputs.VectorDefaults["g_vScale"]).IsEqualTo(new Vector4(2f, 2f, 0f, 0f));
            await Assert.That(parsed.ExpressionInputs.FloatDefaults["g_flRotation"]).IsEqualTo(0f);
            await Assert.That(parsed.Expressions.ContainsKey("g_vScale")).IsFalse();
        }

        [Test]
        [Arguments("g_flA = 1;\ng_flA = 2;\n", "Line 11")]
        [Arguments("g_flA = 1;\n\ng_flB = 2\n", "Line 12")]
        [Arguments("g_flA = 1;\n\ng_flB == 2;\n", "Line 12")]
        [Arguments("\ng_flA = max(1);\n", "Line 11")]
        public async Task TestErrorsNameTheLine(string source, string line)
        {
            await Assert.That(() => Compile(source)).Throws<ShaderLoader.ShaderCompilerException>().WithMessageContaining(line, StringComparison.Ordinal);
        }
    }
}
