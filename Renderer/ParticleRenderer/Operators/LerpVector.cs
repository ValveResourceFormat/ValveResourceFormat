using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpVector : ParticleFunctionOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 output = Vector3.Zero;
        private readonly float startTime;
        private readonly float endTime = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public LerpVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            output = parse.Vector3("m_nInputValue", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                // The set method affects the value the vector is interpolating to, instead of the current interpolated value.
                var lerpTarget = particle.ModifyVectorBySetMethod(particles, FieldOutput, output, setMethod);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = Vector3.Lerp(particle.GetInitialVector(particles, FieldOutput), lerpTarget, lerpWeight);

                particle.SetVector(FieldOutput, scalarOutput);
            }
        }
    }
}
