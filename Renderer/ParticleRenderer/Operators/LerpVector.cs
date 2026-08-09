namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Lerps a vector particle attribute toward a target vector over a specified time window of
    /// the particle's normalized lifetime age.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpVector">C_OP_LerpVector</seealso>
    class LerpVector : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 output = Vector3.Zero;
        private readonly float startTime;
        private readonly float endTime = 1f;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public LerpVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            output = parse.Vector3("m_vecOutput", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                // The set method affects the value the vector is interpolating to, instead of the current interpolated value.
                var lerpTarget = particle.ModifyVectorBySetMethod(particles, fieldOutput, output, setMethod);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.NormalizedAge, startTime, endTime)) * strength;

                // The lerp runs from the spawn initial only for the two initial-value set methods
                var lerpBase = setMethod is ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE
                    ? particle.GetInitialVector(particles, fieldOutput)
                    : particle.GetVector(fieldOutput);

                var scalarOutput = Vector3.Lerp(lerpBase, lerpTarget, lerpWeight);

                particle.SetVector(fieldOutput, scalarOutput);
            }
        }
    }
}
