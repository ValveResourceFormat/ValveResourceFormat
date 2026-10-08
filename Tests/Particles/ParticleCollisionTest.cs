using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Particles;

namespace Tests.Particles
{
    public class ParticleCollisionTest
    {
        /// <summary>A flat floor at a height, hit from above.</summary>
        private sealed class Floor(float height) : IParticleCollision
        {
            public bool HasGeometry => true;

            public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out ParticleTraceHit hit)
            {
                hit = default;

                if (rayStart.Z < height || rayEnd.Z >= height)
                {
                    return false;
                }

                var fraction = (rayStart.Z - height) / (rayStart.Z - rayEnd.Z);
                hit = new ParticleTraceHit(Vector3.Lerp(rayStart, rayEnd, fraction), Vector3.UnitZ, fraction);
                return true;
            }
        }

        private static ParticleSystemSimulation Run(string constraint, float seconds, IParticleCollision collision)
        {
            var simulation = ParticleTestSystem.Simulate($$"""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 10
                    m_flConstantRadius = 2.0
                    m_flConstantLifespan = 10.0
                    m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 4.0 } } ]
                    m_Operators = [ { _class = "C_OP_BasicMovement" m_Gravity = [ 0.0, 0.0, -800.0 ] } ]
                    m_Constraints = [ {{constraint}} ]
                }
                """);
            simulation.RenderState.Collision = collision;

            for (var time = 0f; time < seconds; time += 1f / 60f)
            {
                simulation.Update(1f / 60f, time);
            }

            return simulation;
        }

        [Test]
        public async Task ParticlesComeToRestOnTheFloor()
        {
            var simulation = Run("""{ _class = "C_OP_WorldTraceConstraint" }""", 1.5f, new Floor(-100f));
            var particles = simulation.Particles.Current.ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(particles.Length).IsEqualTo(4);
                await Assert.That(particles.All(p => MathF.Abs(p.Position.Z - -98f) < 0.5f)).IsTrue();
            }
        }

        [Test]
        public async Task WithoutGeometryParticlesFallThrough()
        {
            var simulation = Run("""{ _class = "C_OP_WorldTraceConstraint" }""", 1.5f, IParticleCollision.None);

            await Assert.That(simulation.Particles.Current.ToArray().All(p => p.Position.Z < -500f)).IsTrue();
        }

        [Test]
        public async Task BouncingParticlesLeaveTheFloorAgain()
        {
            // Sample just after the first impact, roughly 0.5s in, while the bounce carries them back up
            var simulation = Run("""{ _class = "C_OP_WorldTraceConstraint" m_flBounceAmount = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 0.8 } }""", 0.62f, new Floor(-100f));
            var particles = simulation.Particles.Current.ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(particles.All(p => p.Position.Z >= -98f - 0.01f)).IsTrue();
                await Assert.That(particles.All(p => p.Position.Z > p.PositionPrevious.Z)).IsTrue();
            }
        }

        [Test]
        public async Task KillOnContactRemovesParticlesAtTheFloor()
        {
            var simulation = Run("""{ _class = "C_OP_WorldTraceConstraint" m_bKillonContact = true }""", 1.5f, new Floor(-100f));

            await Assert.That(simulation.Particles.Count).IsEqualTo(0);
        }

        [Test]
        public async Task PlaceOnGroundDropsSpawnsOntoTheFloor()
        {
            var simulation = ParticleTestSystem.Simulate("""
                {
                    _class = "CParticleSystemDefinition"
                    m_nMaxParticles = 10
                    m_Emitters = [ { _class = "C_OP_InstantaneousEmitter" m_nParticlesToEmit = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 3.0 } } ]
                    m_Initializers =
                    [
                        { _class = "C_INIT_InitFloat" m_InputValue = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 10.0 } m_nOutputField = 1 },
                        { _class = "C_INIT_PositionPlaceOnGround" m_flOffset = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 5.0 } m_bSetNormal = true },
                    ]
                }
                """);
            simulation.RenderState.Collision = new Floor(-60f);
            simulation.Update(1f / 60f, 0f);

            var particles = simulation.Particles.Current.ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(particles.Length).IsEqualTo(3);
                await Assert.That(particles.All(p => MathF.Abs(p.Position.Z - -55f) < 1e-3f)).IsTrue();
                await Assert.That(particles.All(p => p.GetVector(ParticleField.Normal) == Vector3.UnitZ)).IsTrue();
            }
        }
    }
}
