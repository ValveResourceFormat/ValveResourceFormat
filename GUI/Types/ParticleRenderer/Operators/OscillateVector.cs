using System;
using System.Numerics;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class OscillateVector : IParticleOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 rateMin;
        private readonly Vector3 rateMax;
        private readonly Vector3 frequencyMin = Vector3.One;
        private readonly Vector3 frequencyMax = Vector3.One;
        private readonly float oscillationMultiplier = 2.0f;
        private readonly float oscillationOffset = 0.5f;
        private readonly bool proportional = true;
        private readonly bool proportionalOp = true;

        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1.0f;
        private readonly float endTimeMax = 1.0f;

        public OscillateVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nField"))
            {
                outputField = keyValues.GetParticleField("m_nField");
            }

            if (keyValues.ContainsKey("m_RateMin"))
            {
                rateMin = keyValues.GetArray<double>("m_RateMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_RateMax"))
            {
                rateMax = keyValues.GetArray<double>("m_RateMax").ToVector3();
            }

            if (keyValues.ContainsKey("m_FrequencyMin"))
            {
                frequencyMin = keyValues.GetArray<double>("m_FrequencyMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_FrequencyMax"))
            {
                frequencyMax = keyValues.GetArray<double>("m_FrequencyMax").ToVector3();
            }

            if (keyValues.ContainsKey("m_flOscMult"))
            {
                oscillationMultiplier = keyValues.GetFloatProperty("m_flOscMult");
            }

            if (keyValues.ContainsKey("m_flOscAdd"))
            {
                oscillationOffset = keyValues.GetFloatProperty("m_flOscAdd");
            }

            if (keyValues.ContainsKey("m_bProportional"))
            {
                proportional = keyValues.GetProperty<bool>("m_bProportional");
            }

            if (keyValues.ContainsKey("m_bProportionalOp"))
            {
                proportionalOp = keyValues.GetProperty<bool>("m_bProportionalOp");
            }

            if (keyValues.ContainsKey("m_flStartTime_min"))
            {
                startTimeMin = keyValues.GetFloatProperty("m_flStartTime_min");
            }

            if (keyValues.ContainsKey("m_flStartTime_max"))
            {
                startTimeMax = keyValues.GetFloatProperty("m_flStartTime_max");
            }

            if (keyValues.ContainsKey("m_flEndTime_min"))
            {
                endTimeMin = keyValues.GetFloatProperty("m_flEndTime_min");
            }

            if (keyValues.ContainsKey("m_flEndTime_max"))
            {
                endTimeMax = keyValues.GetFloatProperty("m_flEndTime_max");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Remove expired particles
            /*var particlesToRemove = particleRates.Keys.Except(particles[i]).ToList();
            foreach (var p in particlesToRemove)
            {
                particleRates.Remove(p);
                particleFrequencies.Remove(p);
            }*/

            // Update remaining particles
            foreach (var particle in particles)
            {
                var rate = GetParticleRate(particle.ParticleCount);
                var frequency = GetParticleFrequency(particle.ParticleCount);

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (particle.BehaviorVersion == 10)
                {
                    var startTime = GetParticleStartTime(particle.ParticleCount);
                    var endTime = GetParticleEndTime(particle.ParticleCount);

                    if (t < startTime)
                    {
                        t = startTime;
                    }
                    else if (t > endTime)
                    {
                        t = endTime;
                    }
                    else
                    {

                    }
                    // todo. refer to fadeandkill
                }

                Vector3 delta;
                delta.X = MathF.Sin(((t * frequency.X * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Y = MathF.Sin(((t * frequency.Y * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Z = MathF.Sin(((t * frequency.Z * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var value = rate * frameTime * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }

        private readonly Dictionary<int, Vector3> particleRates = new();
        private readonly Dictionary<int, Vector3> particleFrequencies = new();

        private readonly Dictionary<int, float> particleStartTimes = new();
        private readonly Dictionary<int, float> particleEndTimes = new();

        private Vector3 GetParticleRate(int particleId)
        {
            if (particleRates.TryGetValue(particleId, out var rate))
            {
                return rate;
            }
            else
            {
                var newRate = MathUtils.RandomBetweenPerComponent(rateMin, rateMax);
                particleRates[particleId] = newRate;
                return newRate;
            }
        }

        private Vector3 GetParticleFrequency(int particleId)
        {
            if (particleFrequencies.TryGetValue(particleId, out var frequency))
            {
                return frequency;
            }
            else
            {
                var newFrequency = MathUtils.RandomBetweenPerComponent(frequencyMin, frequencyMax);
                particleFrequencies[particleId] = newFrequency;
                return newFrequency;
            }
        }

        private float GetParticleStartTime(int particleID)
        {
            if (particleStartTimes.TryGetValue(particleID, out var startTime))
            {
                return startTime;
            }
            else
            {
                var newStartTime = MathUtils.RandomBetween(startTimeMin, startTimeMax);
                particleStartTimes[particleID] = newStartTime;
                return newStartTime;
            }
        }
        private float GetParticleEndTime(int particleID)
        {
            if (particleEndTimes.TryGetValue(particleID, out var endTime))
            {
                return endTime;
            }
            else
            {
                var newEndTime = MathUtils.RandomBetween(endTimeMin, endTimeMax);
                particleEndTimes[particleID] = newEndTime;
                return newEndTime;
            }
        }
    }

    class OscillateVectorSimple : IParticleOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 rate;
        private readonly Vector3 frequency = Vector3.One;
        private readonly float oscillationMultiplier = 2.0f;
        private readonly float oscillationOffset = 0.5f;

        public OscillateVectorSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nField"))
            {
                outputField = keyValues.GetParticleField("m_nField");
            }

            if (keyValues.ContainsKey("m_Rate"))
            {
                rate = keyValues.GetArray<double>("m_Rate").ToVector3();
            }

            if (keyValues.ContainsKey("m_Frequency"))
            {
                frequency = keyValues.GetArray<double>("m_Frequency").ToVector3();
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
            foreach (var particle in particles)
            {
                Vector3 delta;
                delta.X = MathF.Sin(((particle.Age * frequency.X * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Y = MathF.Sin(((particle.Age * frequency.Y * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Z = MathF.Sin(((particle.Age * frequency.Z * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var value = rate * frameTime * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }
    }

}
