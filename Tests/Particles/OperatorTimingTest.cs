using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Particles
{
    public class OperatorTimingTest
    {
        private static ParticleFunctionOperator CreateOperator(string body)
        {
            var system = ParticleTestSystem.Parse($$"""{ _class = "CParticleSystemDefinition" m_Operators = [ { {{body}} } ] }""");
            var data = system.GetUpgradedData().GetArray("m_Operators")![0];

            ParticleControllerFactory.TryCreateOperator("C_OP_Decay", data, NullLogger.Instance, 12, out var function);
            return function!;
        }

        private static float[] Strengths(ParticleFunctionOperator function, int seed)
        {
            var state = new ParticleSystemState();
            state.Random.PinSeed(seed);

            return [.. Enumerable.Range(0, 40).Select(i =>
            {
                state.Age = i * 0.05f;
                return function.GetOperatorRunStrength(state);
            })];
        }

        [Test]
        public async Task TimeOffsetSeedShiftsTheFadePerInstance()
        {
            // On for the first half second of every second
            var seeded = CreateOperator("""
                _class = "C_OP_Decay"
                m_flOpEndFadeOutTime = 0.5
                m_flOpStartFadeOutTime = 0.5
                m_flOpFadeOscillatePeriod = 1.0
                m_nOpTimeOffsetSeed = 7
                m_flOpTimeOffsetMin = 0.0
                m_flOpTimeOffsetMax = 1.0
                """);

            var unseeded = CreateOperator("""
                _class = "C_OP_Decay"
                m_flOpEndFadeOutTime = 0.5
                m_flOpStartFadeOutTime = 0.5
                m_flOpFadeOscillatePeriod = 1.0
                m_flOpTimeOffsetMin = 0.0
                m_flOpTimeOffsetMax = 1.0
                """);

            var instanceA = Strengths(seeded, 100);
            var instanceAAgain = Strengths(seeded, 100);
            var instanceB = Strengths(seeded, 2000);

            using (Assert.Multiple())
            {
                await Assert.That(instanceAAgain.SequenceEqual(instanceA)).IsTrue();
                await Assert.That(instanceB.SequenceEqual(instanceA)).IsFalse();

                // Without a seed the offset range is ignored, whatever the instance
                await Assert.That(Strengths(unseeded, 100).SequenceEqual(Strengths(unseeded, 2000))).IsTrue();
                await Assert.That(Strengths(unseeded, 100)[0]).IsEqualTo(1f);
            }
        }
    }
}
