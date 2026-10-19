using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;

namespace Tests
{
    public class ParticleOrient2DRelToCPTest
    {
        [Test]
        [Arguments(10f, 0f, 0f, MathF.PI)]
        [Arguments(0f, 10f, 90f, 2f * MathF.PI)]
        [Arguments(-10f, 0f, -45f, 5.4977875f)]
        [Arguments(0f, -10f, 30f, 2.0943952f)]
        [Arguments(3f, 4f, 0f, 4.0688877f)]
        public async Task OrientsTowardTheSelectedCurrentControlPoint(float x, float y, float offset, float expected)
        {
            var initializer = Create(3, offset, ParticleField.Roll);
            var state = new ParticleSystemState();
            state.GetControlPoint(3).Position = new Vector3(2f, 5f, -70f);
            state.GetControlPoint(3).Orientation = -Vector3.UnitX;
            var particle = new Particle { Position = new Vector3(x + 2f, y + 5f, 99f), Rotation = new Vector3(1f, 2f, 0.75f) };

            initializer.Initialize(ref particle, new ParticleCollection(new Particle(), 1), state);

            await Assert.That(particle.Rotation).IsEqualTo(new Vector3(1f, 2f, expected));
            await Assert.That(initializer.WrittenFields).IsEqualTo(1UL << (int)ParticleField.Roll);
        }

        [Test]
        public async Task LeavesTheOutputUntouchedWhenOnlyHeightDiffers()
        {
            var initializer = Create(0, 90f, ParticleField.Yaw);
            var particle = new Particle { Position = new Vector3(0f, 0f, 100f), Rotation = new Vector3(1f, 2f, 3f) };

            initializer.Initialize(ref particle, new ParticleCollection(new Particle(), 1), new ParticleSystemState());

            await Assert.That(particle.Rotation).IsEqualTo(new Vector3(1f, 2f, 3f));
        }

        [Test]
        public async Task WritesTheRequestedRotationField()
        {
            var initializer = Create(0, 0f, ParticleField.Yaw);
            var particle = new Particle { Position = Vector3.UnitX, Rotation = new Vector3(1f, 2f, 3f) };

            initializer.Initialize(ref particle, new ParticleCollection(new Particle(), 1), new ParticleSystemState());

            await Assert.That(particle.Rotation).IsEqualTo(new Vector3(MathF.PI, 2f, 3f));
            await Assert.That(initializer.WrittenFields).IsEqualTo(1UL << (int)ParticleField.Yaw);
        }

        [Test]
        public async Task RetainsTheNonFiniteResultOfOverflowingNormalization()
        {
            var initializer = Create(0, 0f, ParticleField.Roll);
            var particle = new Particle { Position = new Vector3(1e20f, 2e20f, 0f) };

            initializer.Initialize(ref particle, new ParticleCollection(new Particle(), 1), new ParticleSystemState());

            await Assert.That(float.IsNaN(particle.Rotation.Z)).IsTrue();
        }

        private static ParticleFunctionInitializer Create(int controlPoint, float offset, ParticleField field)
        {
            var data = KVObject.Collection();
            data["m_nCP"] = new KVObject(controlPoint);
            data["m_flRotOffset"] = new KVObject(offset);
            data["m_nFieldOutput"] = new KVObject((int)field);
            if (!ParticleControllerFactory.TryCreateInitializer("C_INIT_Orient2DRelToCP", data, NullLogger.Instance, 12, out var initializer))
            {
                throw new InvalidOperationException("The initializer must be registered");
            }

            return initializer;
        }
    }
}
