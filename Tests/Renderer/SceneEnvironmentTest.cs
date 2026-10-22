using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Renderer
{
    public class SceneEnvironmentTest
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public async Task PatternBlendsLettersAndLoops()
        {
            var style = new PatternLightStyle("amz");

            await Assert.That(style.Duration).IsEqualTo(0.3f).Within(Tolerance);
            await Assert.That(style.GetBrightness(0.05f)).IsEqualTo(0.5f).Within(Tolerance);
            await Assert.That(style.GetBrightness(0.1f)).IsEqualTo(1f).Within(Tolerance);
            await Assert.That(style.GetBrightness(0.25f)).IsEqualTo(25f / 24f).Within(Tolerance);
        }

        [Test]
        public async Task CurveFollowsTangentsAndSortsEvents()
        {
            const string Kv = """
                <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
                {
                    events = [ { event = "spark" fire_time = 1.5 }, { event = "on" fire_time = 0.5 } ]
                    dimmer =
                    {
                        m_spline = [ { x = 0.0 y = 0.0 }, { x = 1.0 y = 2.0 }, { x = 2.0 y = 0.0 } ]
                        m_tangents =
                        [
                            { m_nIncomingTangent = "CURVE_TANGENT_LINEAR" m_nOutgoingTangent = "CURVE_TANGENT_LINEAR" },
                            { m_nIncomingTangent = "CURVE_TANGENT_SPLINE" m_nOutgoingTangent = "CURVE_TANGENT_SPLINE" },
                            { m_nIncomingTangent = "CURVE_TANGENT_LINEAR" m_nOutgoingTangent = "CURVE_TANGENT_LINEAR" },
                        ]
                        m_vDomainMins = [ 0.0, 0.0 ]
                        m_vDomainMaxs = [ 2.0, 1.5 ]
                    }
                }
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Kv));
            var style = new CurveLightStyle(KVDocumentExtensions.ParseKV3(stream).Root);

            await Assert.That(style.Duration).IsEqualTo(2f).Within(Tolerance);
            await Assert.That(style.Events[0]).IsEqualTo(new LightStyleEvent("on", 0.5f));
            await Assert.That(style.GetBrightness(0.5f)).IsEqualTo(1.25f).Within(Tolerance);

            // Clamped to domain max
            await Assert.That(style.GetBrightness(1f)).IsEqualTo(1.5f).Within(Tolerance);
        }
    }
}
