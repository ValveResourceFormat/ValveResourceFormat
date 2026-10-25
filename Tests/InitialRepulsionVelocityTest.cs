using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;

namespace Tests
{
    public class InitialRepulsionVelocityTest
    {
        private static InitialRepulsionVelocity CreateInitializer(bool proportional)
        {
            var data = KVObject.ListCollection();
            data["m_bInherit"] = new KVObject(true);
            data["m_bProportional"] = new KVObject(proportional);
            data["m_bTranslate"] = new KVObject(true);
            return new InitialRepulsionVelocity(new ParticleDefinitionParser(data, NullLogger.Instance));
        }

        private static ParticleSystemState CreateState()
        {
            var state = new ParticleSystemState();
            state.GetControlPoint(0).Position = new Vector3(1f, -2f, 3f);
            state.GetControlPoint(1).Position = Vector3.UnitX;
            return state;
        }

        [Test]
        public async Task ProportionalOffsetAccumulatesMixedRadii()
        {
            var initializer = CreateInitializer(true);
            var state = CreateState();
            var particles = new ParticleCollection(new Particle(), 4);
            Particle[] batch =
            [
                new() { Radius = 2f, Position = Vector3.One, PositionPrevious = -Vector3.One },
                new() { Radius = 0.5f, Position = Vector3.One, PositionPrevious = -Vector3.One },
                new() { Radius = 3f, Position = Vector3.One, PositionPrevious = -Vector3.One },
                new() { Radius = 0.25f, Position = Vector3.One, PositionPrevious = -Vector3.One },
            ];

            initializer.Initialize(batch.AsSpan(), particles, state);

            float[] scales = [2f, 1f, 3f, 0.75f];
            using (Assert.Multiple())
            {
                for (var i = 0; i < batch.Length; i++)
                {
                    var offset = new Vector3(1f, -2f, 3f) * scales[i];
                    await Assert.That(batch[i].Position).IsEqualTo(Vector3.One + offset);
                    await Assert.That(batch[i].PositionPrevious).IsEqualTo(-Vector3.One + offset);
                }
            }
        }

        [Test]
        public async Task ProportionalOffsetResetsForEveryCall()
        {
            var initializer = CreateInitializer(true);
            var state = CreateState();
            var particles = new ParticleCollection(new Particle(), 18);
            var batch = new Particle[17];
            for (var i = 0; i < batch.Length; i++)
            {
                batch[i] = new Particle { Index = i + 1, Radius = 2f };
            }

            initializer.Initialize(batch.AsSpan(), particles, state);

            float[] scales = [2f, 4f, 8f, 2f, 4f, 8f, 16f, 32f, 64f, 128f, 256f, 512f, 1024f, 2048f, 4096f, 2f, 4f];
            using (Assert.Multiple())
            {
                for (var i = 0; i < batch.Length; i++)
                {
                    var offset = new Vector3(1f, -2f, 3f) * scales[i];
                    await Assert.That(batch[i].Position).IsEqualTo(offset);
                    await Assert.That(batch[i].PositionPrevious).IsEqualTo(offset);
                }
            }

            state.GetControlPoint(0).Position = new Vector3(4f, 2f, -1f);
            Particle[] nextBatch = [new() { Radius = 0.5f }, new() { Radius = 3f }];
            initializer.Initialize(nextBatch.AsSpan(), particles, state);

            using (Assert.Multiple())
            {
                await Assert.That(nextBatch[0].Position).IsEqualTo(new Vector3(2f, 1f, -0.5f));
                await Assert.That(nextBatch[0].PositionPrevious).IsEqualTo(new Vector3(2f, 1f, -0.5f));
                await Assert.That(nextBatch[1].Position).IsEqualTo(new Vector3(6f, 3f, -1.5f));
                await Assert.That(nextBatch[1].PositionPrevious).IsEqualTo(new Vector3(6f, 3f, -1.5f));
            }
        }

        [Test]
        public async Task NonProportionalOffsetIgnoresRadii()
        {
            var initializer = CreateInitializer(false);
            var state = CreateState();
            var particles = new ParticleCollection(new Particle(), 3);
            Particle[] batch = [new() { Radius = 2f }, new() { Radius = 0.5f }, new() { Radius = 3f }];

            initializer.Initialize(batch.AsSpan(), particles, state);

            using (Assert.Multiple())
            {
                foreach (var particle in batch)
                {
                    await Assert.That(particle.Position).IsEqualTo(new Vector3(1f, -2f, 3f));
                    await Assert.That(particle.PositionPrevious).IsEqualTo(new Vector3(1f, -2f, 3f));
                }
            }
        }
    }
}
