using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Utils;

namespace Tests.Particles
{
    public class RemapDotProductThresholdTest
    {
        [Test]
        public async Task OppositeDirectionsReachTheInputThreshold()
        {
            var definition = new KVObject
            {
                ["m_nInputCP2"] = 12,
                ["m_nFieldOutput"] = 27,
                ["m_flInputMin"] = -1f,
                ["m_flInputMax"] = -1f,
                ["m_flOutputMin"] = -1f,
            };
            var operation = new RemapDotProductToScalar(new ParticleDefinitionParser(definition, NullLogger.Instance, new()));
            var particles = new ParticleCollection(new ParticleBuffer.ConstantValues(), 1);
            particles.SetStorage(ulong.MaxValue, 0, 0);
            particles.Add();
            var state = new ParticleSystemState();
            state.SetControlPointOrientation(0, new Vector3(0.9087155f, 0.41598785f, -0.034498483f), Vector3.UnitY, Vector3.UnitZ);
            state.SetControlPointOrientation(12, new Vector3(-0.90871567f, -0.41598788f, 0.034498498f), Vector3.UnitY, Vector3.UnitZ);

            operation.Operate(particles, 1f / 60f, state, 1f);

            await Assert.That(particles.Current.ReadFloat(ParticleField.ScratchFloat2, 0)).IsEqualTo(1f);
        }
    }
}
