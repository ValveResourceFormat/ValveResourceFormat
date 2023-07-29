using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpVector : IParticleOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 output = Vector3.Zero;
        private readonly float startTime;
        private readonly float endTime = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public LerpVector(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);

            output = parse.Vector3("m_nInputValue", output);

            startTime = parse.Float("m_flStartTime", startTime);

            endTime = parse.Float("m_flEndTime", endTime);

            if (parse.Data.ContainsKey("m_nSetMethod"))
            {
                setMethod = parse.Data.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // The set method affects the value the vector is interpolating to, instead of the current interpolated value.
                var lerpTarget = particle.ModifyVectorBySetMethod(FieldOutput, output, setMethod);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = MathUtils.Lerp(lerpWeight, particle.GetInitialVector(FieldOutput), lerpTarget);

                particle.SetVector(FieldOutput, scalarOutput);
            }
        }
    }
}
