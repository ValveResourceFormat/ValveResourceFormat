using System;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class OscillateScalar : IParticleOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rateMin;
        private readonly float rateMax;
        private readonly float frequencyMin = 1f;
        private readonly float frequencyMax = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;
        private readonly bool proportional = true;

        public OscillateScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nField"))
            {
                outputField = keyValues.GetParticleField("m_nField");
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
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Remove expired particles
            /*var particlesToRemove = particleRates.Keys.Except(particle).ToList();
            foreach (var p in particlesToRemove)
            {
                particleRates.Remove(p);
                particleFrequencies.Remove(p);
            }*/

            // Update remaining particles
            foreach (ref var particle in particles)
            {
                var rate = GetParticleRate(particle.ParticleCount);
                var frequency = GetParticleFrequency(particle.ParticleCount);

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                var delta = MathF.Sin(((t * frequency * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var finalScalar = delta * rate * frameTime;
                particle.SetScalar(outputField, particle.GetScalar(outputField) + finalScalar);
            }
        }

        private readonly Dictionary<int, float> particleRates = new();
        private readonly Dictionary<int, float> particleFrequencies = new();

        private float GetParticleRate(int particleId)
        {
            if (particleRates.TryGetValue(particleId, out var rate))
            {
                return rate;
            }
            else
            {
                var newRate = MathUtils.RandomBetween(rateMin, rateMax);
                particleRates[particleId] = newRate;
                return newRate;
            }
        }

        private float GetParticleFrequency(int particleId)
        {
            if (particleFrequencies.TryGetValue(particleId, out var frequency))
            {
                return frequency;
            }
            else
            {
                var newFrequency = MathUtils.RandomBetween(frequencyMin, frequencyMax);
                particleFrequencies[particleId] = newFrequency;
                return newFrequency;
            }
        }
    }

    class OscillateScalarSimple : IParticleOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rate;
        private readonly float frequency = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;

        public OscillateScalarSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nField"))
            {
                outputField = keyValues.GetParticleField("m_nField");
            }

            if (keyValues.ContainsKey("m_Rate"))
            {
                rate = keyValues.GetFloatProperty("m_Rate");
            }

            if (keyValues.ContainsKey("m_Frequency"))
            {
                frequency = keyValues.GetFloatProperty("m_Frequency");
            }

            if (keyValues.ContainsKey("m_flOscMult"))
            {
                oscillationMultiplier = keyValues.GetFloatProperty("m_flOscMult");
            }

            if (keyValues.ContainsKey("m_flOscAdd"))
            {
                oscillationOffset = keyValues.GetFloatProperty("m_flOscAdd");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Update remaining particles
            foreach (ref var particle in particles)
            {
                var delta = MathF.Sin(((particle.Age * frequency * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var finalScalar = delta * rate * frameTime;

                particle.SetScalar(outputField, particle.GetScalar(outputField) + finalScalar);
            }
        }
    }
}
