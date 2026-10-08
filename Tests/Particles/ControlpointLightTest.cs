using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Particles.Operators;

namespace Tests.Particles
{
    public class ControlpointLightTest
    {
        [Test]
        public async Task FalloffPassesHalfAtFiftyAndNothingAtZero()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ControlpointLight.Attenuation(0f, 100f, 200f)).IsEqualTo(1f);
                await Assert.That(ControlpointLight.Attenuation(100f, 100f, 200f)).IsEqualTo(0.5f).Within(1e-6f);
                await Assert.That(ControlpointLight.Attenuation(200f, 100f, 200f)).IsEqualTo(0f);
                await Assert.That(ControlpointLight.Attenuation(500f, 100f, 200f)).IsEqualTo(0f);
                await Assert.That(ControlpointLight.Attenuation(150f, 100f, 200f)).IsLessThan(ControlpointLight.Attenuation(120f, 100f, 200f));

                // Without a 50% distance the light only ramps down to its 0% distance
                await Assert.That(ControlpointLight.Attenuation(50f, 0f, 100f)).IsEqualTo(0.5f).Within(1e-6f);
            }
        }

        private const string LitSystem = """
            {
                _class = "CParticleSystemDefinition"
                m_nBehaviorVersion = 12
                m_nMaxParticles = 8
                m_ConstantColor = [ 10, 10, 10, 255 ]
                m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 2.0 } } ]
                m_Initializers =
                [
                    { _class = "C_INIT_InitFloat" m_InputValue = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 10.0 } m_nOutputField = 1 },
                ]
                m_Operators =
                [
                    {
                        _class = "C_OP_ControlpointLight"
                        m_nControlPoint1 = 1
                        m_vecCPOffset1 = [ 0.0, 0.0, 50.0 ]
                        m_LightColor1 = [ 255, 128, 0 ]
                        m_LightFiftyDist1 = 100.0
                        m_LightZeroDist1 = 1000.0
                        m_flScale = 1.0
                        m_bClampLowerRange = true
                    },
                ]
            }
            """;

        [Test]
        public async Task LightsAddOnTopOfTheBiasedInitialColor()
        {
            var simulation = ParticleTestSystem.Simulate(LitSystem);
            simulation.RenderState.SetControlPointValue(1, new Vector3(0f, 0f, 0f));

            simulation.Update(1f / 60f, 0f);

            var particles = simulation.Particles.Current.ToArray();
            var initial = new Vector3(10f / 255f);

            // The particles spawn on control point 0, 50 units from the light and inside its 50% distance,
            // so the inverse square alone gives 1 / (1 + 0.5^2)
            var expected = 1f / 1.25f;

            using (Assert.Multiple())
            {
                await Assert.That(particles.Length).IsEqualTo(2);

                foreach (var particle in particles)
                {
                    await Assert.That(particle.Color.X).IsEqualTo(initial.X + expected).Within(1e-3f);
                    await Assert.That(particle.Color.Y).IsEqualTo(initial.Y + (expected * 128f / 255f)).Within(1e-3f);
                    await Assert.That(particle.Color.Z).IsEqualTo(initial.Z).Within(1e-3f);
                }
            }
        }
    }
}
