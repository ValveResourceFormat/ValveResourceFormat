using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Renderer
{
    public class LightStyleTest
    {
        [Test]
        public async Task PatternBlendsBetweenLetters()
        {
            var style = new PatternLightStyle("am");

            await Assert.That(style.Evaluate(0f).X).IsEqualTo(0f);
            await Assert.That(style.Evaluate(0.1f).X).IsEqualTo(1f).Within(0.0001f);
            await Assert.That(style.Evaluate(0.05f).X).IsEqualTo(0.5f).Within(0.0001f);

            // Wraps back to the first letter
            await Assert.That(style.Evaluate(0.15f).X).IsEqualTo(0.5f).Within(0.0001f);
        }

        [Test]
        public async Task DimmerLoopsOverItsDomain()
        {
            const string Text = """
                <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
                {
                    dimmer =
                    {
                        m_spline = [ { x = 0.0 y = 0.0 }, { x = 2.0 y = 1.0 } ]
                        m_tangents =
                        [
                            { m_nIncomingTangent = "CURVE_TANGENT_LINEAR" m_nOutgoingTangent = "CURVE_TANGENT_LINEAR" },
                            { m_nIncomingTangent = "CURVE_TANGENT_LINEAR" m_nOutgoingTangent = "CURVE_TANGENT_LINEAR" },
                        ]
                        m_vDomainMins = [ 0.0, 0.0 ]
                        m_vDomainMaxs = [ 2.0, 1.0 ]
                    }
                }
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Text));
            var style = new CurveLightStyle(KVDocumentExtensions.ParseKV3(stream).Root);

            await Assert.That(style.Evaluate(1f).X).IsEqualTo(0.5f).Within(0.0001f);
            await Assert.That(style.Evaluate(3f).X).IsEqualTo(0.5f).Within(0.0001f);

            // A whole loop lands on the end of the curve, not its start
            await Assert.That(style.Evaluate(2f).X).IsEqualTo(1f).Within(0.0001f);
        }
    }
}
