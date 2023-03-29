using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomSequence : IParticleInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;
        private int counter;

        public RandomSequence(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nSequenceMin"))
            {
                sequenceMin = (int)keyValues.GetIntegerProperty("m_nSequenceMin");
            }

            if (keyValues.ContainsKey("m_nSequenceMax"))
            {
                sequenceMax = (int)keyValues.GetIntegerProperty("m_nSequenceMax");
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
