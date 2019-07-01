using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class OscillateScalar : IParticleOperator
    {
        private const int ParticleFieldRadius = 3;
        private const int ParticleFieldAlpha = 7;
        private const int ParticleFieldAlphaAlternate = 16;

        private int outputField = ParticleFieldAlpha;
        private float rateMin = 0f;
        private float rateMax = 0f;
        private float frequencyMin = 1f;
        private float frequencyMax = 1f;
        private float oscillationMultiplier = 2f;
        private float oscillationOffset = 0.5f;
        private bool proportional = true;

        private Random random;

        public OscillateScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nField"))
            {
                outputField = (int)keyValues.GetIntegerProperty("m_nField");
            }

            if (keyValues.ContainsKey("m_RateMin"))
            {
                rateMin = keyValues.GetFloatProperty("m_RateMin");
            }

            if (keyValues.ContainsKey("m_RateMax"))
            {
                rateMax = keyValues.GetFloatProperty("m_RateMax");
            }

            if (keyValues.ContainsKey("m_FrequencyMin"))
            {
                frequencyMin = keyValues.GetFloatProperty("m_FrequencyMin");
            }

            if (keyValues.ContainsKey("m_FrequencyMax"))
            {
                frequencyMax = keyValues.GetFloatProperty("m_FrequencyMax");
            }

            if (keyValues.ContainsKey("m_flOscMult"))
            {
                oscillationMultiplier = keyValues.GetFloatProperty("m_flOscMult");
            }

            if (keyValues.ContainsKey("m_flOscAdd"))
            {
                oscillationOffset = keyValues.GetFloatProperty("m_flOscAdd");
            }

            if (keyValues.ContainsKey("m_bProportionalOp"))
            {
                proportional = keyValues.GetProperty<bool>("m_bProportionalOp");
            }

            random = new Random();
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Remove expired particles
            var particlesToRemove = particleRates.Keys.Except(particles).ToList();
            foreach (var p in particlesToRemove)
            {
                particleRates.Remove(p);
                particleFrequencies.Remove(p);
            }

            // Update remaining particles
            foreach (var particle in particles)
            {
                var rate = GetParticleRate(particle);
                var frequency = GetParticleFrequency(particle);

                var t = proportional
                    ? 1 - (particle.Lifetime / particle.ConstantLifetime)
                    : particle.Lifetime;

                var delta = (float)Math.Sin(((t * frequency * oscillationMultiplier) + oscillationOffset) * Math.PI);

                if (outputField == ParticleFieldRadius)
                {
                    particle.Radius += delta * rate * frameTime;
                }
                else if (outputField == ParticleFieldAlpha)
                {
                    particle.Alpha += delta * rate * frameTime;
                }
                else if (outputField == ParticleFieldAlphaAlternate)
                {
                    particle.AlphaAlternate += delta * rate * frameTime;
                }
            }
        }

        private Dictionary<Particle, float> particleRates = new Dictionary<Particle, float>();
        private Dictionary<Particle, float> particleFrequencies = new Dictionary<Particle, float>();

        private float GetParticleRate(Particle particle)
        {
            if (particleRates.TryGetValue(particle, out var rate))
            {
                return rate;
            }
            else
            {
                var newRate = rateMin + ((float)random.NextDouble() * (rateMax - rateMin));
                particleRates[particle] = newRate;
                return newRate;
            }
        }

        private float GetParticleFrequency(Particle particle)
        {
            if (particleFrequencies.TryGetValue(particle, out var frequency))
            {
                return frequency;
            }
            else
            {
                var newFrequency = frequencyMin + ((float)random.NextDouble() * (frequencyMax - frequencyMin));
                particleFrequencies[particle] = newFrequency;
                return newFrequency;
            }
        }
    }
}
