using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomSequence : IParticleInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;

        private int counter;

        // In Behavior Ver 12+ there is a "weight list" that weights the randomness
        public RandomSequence(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nSequenceMin"))
            {
                sequenceMin = keyValues.GetInt32Property("m_nSequenceMin");
            }

            if (keyValues.ContainsKey("m_nSequenceMax"))
            {
                sequenceMax = keyValues.GetInt32Property("m_nSequenceMax");
            }

            if (keyValues.ContainsKey("m_bShuffle"))
            {
                shuffle = keyValues.GetProperty<bool>("m_bShuffle");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (shuffle)
            {
                particle.Sequence = Random.Shared.Next(sequenceMin, sequenceMax + 1);
            }
            else
            {
                particle.Sequence = sequenceMin + (sequenceMax > sequenceMin ? (counter++ % (sequenceMax - sequenceMin)) : 0);
            }

            return particle;
        }
    }
}
