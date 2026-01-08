namespace ValveResourceFormat.Renderer.Particles.Operators
{
    class LerpScalar : ParticleFunctionOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly INumberProvider output = new LiteralNumberProvider(1);
        private readonly float startTime;
        private readonly float endTime = 1f;

        public LerpScalar(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            output = parse.NumberProvider("m_flOutput", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var lerpTarget = output.NextNumber(ref particle, particleSystemState);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = float.Lerp(particle.GetInitialScalar(particles, FieldOutput), lerpTarget, lerpWeight);

                particle.SetScalar(FieldOutput, scalarOutput);
            }
        }
    }
}
