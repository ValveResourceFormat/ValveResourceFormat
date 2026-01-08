namespace ValveResourceFormat.Renderer.Particles.Operators
{
    class RemapControlPointDirectionToVector : ParticleFunctionOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly int cp;
        private readonly float scale;

        public RemapControlPointDirectionToVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            scale = parse.Float("m_flScale", scale);
            cp = parse.Int32("m_nControlPointNumber", cp);
        }

        // is this particle id or total particle count?
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                // direction or orientation??
                var direction = particleSystemState.GetControlPoint(cp).Orientation;
                particle.SetVector(FieldOutput, direction * scale);
            }
        }
    }
}
