using System.Linq;
using System.Threading.Tasks;

namespace Tests.Particles
{
    public class ParticleFunctionBatchTest
    {
        private static ValveResourceFormat.Particles.ParticleSystemSimulation Run(string definition, float seconds)
        {
            var simulation = ParticleTestSystem.Simulate(definition);

            for (var time = 0f; time < seconds; time += 1f / 60f)
            {
                simulation.Update(1f / 60f, time);
            }

            return simulation;
        }

        [Test]
        public async Task MaintainEmitterRefillsToItsCount()
        {
            // Particles live a tenth of a second, so only refilling keeps the count up
            var simulation = Run("""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 100
                    m_flConstantLifespan = 0.1
                    m_Emitters = [ { _class = "C_OP_MaintainEmitter" m_nParticlesToMaintain = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 12.0 } } ]
                    m_Operators = [ { _class = "C_OP_Decay" } ]
                }
                """, 1f);

            await Assert.That(simulation.Particles.Count).IsEqualTo(12);
        }

        [Test]
        public async Task PlanarConstraintPushesParticlesAboveThePlane()
        {
            var simulation = Run("""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 10
                    m_flConstantRadius = 4.0
                    m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 5.0 } } ]
                    m_Initializers = [ { _class = "C_INIT_InitFloat" m_InputValue = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 10.0 } m_nOutputField = 1 } ]
                    m_Operators = [ { _class = "C_OP_BasicMovement" m_Gravity = [ 0.0, 0.0, -800.0 ] } ]
                    m_Constraints = [ { _class = "C_OP_PlanarConstraint" m_PointOnPlane = [ 0.0, 0.0, -50.0 ] } ]
                }
                """, 2f);

            var heights = simulation.Particles.Current.ToArray().Select(p => p.Position.Z).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(heights.Length).IsEqualTo(5);
                await Assert.That(heights.All(z => MathF.Abs(z - -46f) < 0.5f)).IsTrue();
            }
        }

        [Test]
        public async Task VelocityFromNormalLaunchesAlongTheNormal()
        {
            var simulation = Run("""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 10
                    m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 3.0 } } ]
                    m_Initializers =
                    [
                        { _class = "C_INIT_InitFloat" m_InputValue = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 10.0 } m_nOutputField = 1 },
                        { _class = "C_INIT_InitVec" m_InputValue = { m_nType = "PVEC_TYPE_LITERAL" m_vLiteralValue = [ 0.0, 1.0, 0.0 ] } m_nOutputField = 21 },
                        { _class = "C_INIT_VelocityFromNormal" m_fSpeedMin = 100.0 m_fSpeedMax = 100.0 },
                    ]
                    m_Operators = [ { _class = "C_OP_BasicMovement" } ]
                }
                """, 0.5f);

            var positions = simulation.Particles.Current.ToArray().Select(p => p.Position).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(positions.Length).IsEqualTo(3);
                await Assert.That(positions.All(p => p.Y > 40f && MathF.Abs(p.X) < 1e-3f && MathF.Abs(p.Z) < 1e-3f)).IsTrue();
            }
        }

        [Test]
        public async Task RadiusDecayKillsShrunkParticles()
        {
            var simulation = Run("""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 10
                    m_flConstantRadius = 0.5
                    m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 4.0 } } ]
                    m_Initializers = [ { _class = "C_INIT_InitFloat" m_InputValue = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 10.0 } m_nOutputField = 1 } ]
                    m_Operators = [ { _class = "C_OP_RadiusDecay" } ]
                }
                """, 0.1f);

            await Assert.That(simulation.Particles.Count).IsEqualTo(0);
        }
    }
}
