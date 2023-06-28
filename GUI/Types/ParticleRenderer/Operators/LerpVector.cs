using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpVector : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly Vector3 output = Vector3.Zero;
        private readonly float startTime;
        private readonly float endTime = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public LerpVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_nInputValue"))
            {
                output = keyValues.GetArray<double>("m_nInputValue").ToVector3();
            }

            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEndTime"))
            {
                endTime = keyValues.GetFloatProperty("m_flEndTime");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                // The set method affects the value the vector is interpolating to, instead of the current interpolated value.
                var lerpTarget = particle.ModifyVectorBySetMethod(field, output, setMethod);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = MathUtils.Lerp(lerpWeight, particle.GetInitialVector(field), lerpTarget);

                particle.SetVector(field, scalarOutput);
            }
        }
    }
}
