namespace ValveResourceFormat.Renderer.Particles.Operators
{
    class SetVec : ParticleFunctionOperator
    {
        private readonly ParticleField OutputField = ParticleField.Color;
        private readonly IVectorProvider value = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetVec(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            value = parse.VectorProvider("m_nInputValue", value);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            lerp = parse.NumberProvider("m_Lerp", lerp);

            // there's also a Lerp value that will fade it in when at low values. Further testing is needed to know anything more
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var value = this.value.NextVector(ref particle, particleSystemState);
                var lerp = this.lerp.NextNumber(ref particle, particleSystemState);

                var currentValue = particle.ModifyVectorBySetMethod(particles, OutputField, value, setMethod);
                var initialValue = particle.GetVector(OutputField);

                value = Vector3.Lerp(initialValue, currentValue, lerp);

                particle.SetVector(OutputField, value);
            }
        }
    }
}
