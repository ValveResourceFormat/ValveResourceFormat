using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class InheritFromParentParticlesV2Test
    {
        [Test]
        public async Task NormalizedParentIsClampedBeforeInterpolation()
        {
            var particles = Inherit(ParticleField.Color, false);

            await Assert.That(particles.Current[0].Color).IsEqualTo(new Vector3(1.125f, -0.25f, 0.4375f));
        }

        [Test]
        public async Task NormalizedNextParentIsClampedBeforeSubsampling()
        {
            var particles = Inherit(ParticleField.Color, true);

            await Assert.That(particles.Current[1].Color).IsEqualTo(new Vector3(1.25f, -0.1875f, 0.3125f));
        }

        [Test]
        public async Task UnnormalizedParentWithoutSubsamplingKeepsScaledValues()
        {
            var particles = Inherit(ParticleField.Position, false);

            using (Assert.Multiple())
            {
                await Assert.That(particles.Current[0].Position).IsEqualTo(new Vector3(1f, -0.25f, 0.5625f));
                await Assert.That(particles.Current[1].Position).IsEqualTo(new Vector3(1f, -0.25f, 0.5625f));
            }
        }

        private static ParticleCollection Inherit(ParticleField field, bool subSample)
        {
            using var fileLoader = new GameFileLoader(null, null);
            var parent = new ParticleSystemSimulation(ParticleSystem.Create(new KVObject()), fileLoader);
            parent.Particles.Add();
            parent.Particles.Add();
            parent.Particles.Current[0].ParticleId = 10;
            parent.Particles.Current[1].ParticleId = 11;
            parent.Particles.Current[0].SetVector(field, new Vector3(-0.25f, 0.25f, 0.75f));
            parent.Particles.Current[1].SetVector(field, new Vector3(0.75f, 2f, -0.5f));

            var particles = new ParticleCollection(new Particle(), 2);
            for (var i = 0; i < 2; i++)
            {
                particles.Add();
                particles.Current[i].ParentParticleIndex = -1;
                particles.Current[i].ParentParticleId = -1;
                particles.Current[i].SetVector(field, new Vector3(1.5f, -0.5f, 0.25f));
            }

            var parameters = new KVObject();
            parameters.Add("m_nFieldOutput", (int)field);
            parameters.Add("m_flScale", 2f);
            parameters.Add("m_flInterpolation", 0.25f);
            parameters.Add("m_nIncrement", 0.5f);
            parameters.Add("m_bSubSample", subSample);

            if (!ParticleControllerFactory.TryCreateOperator("C_OP_InheritFromParentParticlesV2", parameters,
                NullLogger.Instance, 0, out var operation))
            {
                throw new InvalidOperationException("InheritFromParentParticlesV2 is not registered.");
            }

            operation.Operate(particles, 0.015625f, new ParticleSystemState(parent.RenderState), 1f);
            return particles;
        }
    }
}
