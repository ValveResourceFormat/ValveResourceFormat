using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class DistanceToCP : IParticleOperator
    {
        private readonly float distanceMin;
        private readonly float distanceMax = 128;
        private readonly float outputMin;
        private readonly float outputMax = 1;
        private readonly int controlPoint;

        private readonly ParticleField field = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool additive;
        private readonly bool activeRange;

        public DistanceToCP(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_flInputMin"))
            {
                distanceMin = keyValues.GetFloatProperty("m_flInputMin");
            }

            if (keyValues.ContainsKey("m_flInputMax"))
            {
                distanceMax = keyValues.GetFloatProperty("m_flInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetFloatProperty("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetFloatProperty("m_flOutputMax");
            }

            if (keyValues.ContainsKey("m_nStartCP"))
            {
                controlPoint = keyValues.GetInt32Property("m_nStartCP");
            }

            if (keyValues.ContainsKey("m_bAdditive"))
            {
                additive = keyValues.GetProperty<bool>("m_bAdditive");
            }

            if (keyValues.ContainsKey("m_bActiveRange"))
            {
                activeRange = keyValues.GetProperty<bool>("m_bActiveRange");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }


            // Unsupported features: LOS test. We'd need collision for that.
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var cpPos = particleSystemState.GetControlPoint(controlPoint).Position;

            foreach (ref var particle in particles)
            {
                var distance = Vector3.Distance(cpPos, particle.Position);

                // presumably triggered by activerange. untested but consistent with other modules behavior
                if (activeRange && (distance < distanceMin || distance > distanceMax))
                {
                    continue;
                }

                var remappedDistance = MathUtils.Remap(distance, distanceMin, distanceMax);

                remappedDistance = MathUtils.Saturate(remappedDistance);

                var finalValue = MathUtils.Lerp(remappedDistance, outputMin, outputMax);

                finalValue = particle.ModifyScalarBySetMethod(field, finalValue, setMethod);

                if (additive)
                {
                    // Yes, this causes it to continuously grow larger. Yes, this is in the original too.
                    finalValue += particle.GetScalar(field);
                }
                particle.SetScalar(field, finalValue);
            }
        }
    }
}
