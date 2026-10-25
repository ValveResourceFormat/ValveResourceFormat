namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the radius of new particles to the scale of the object a control point is bound to, 1
    /// when it is bound to none, times the initializer's strength.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RadiusFromCPObject">C_INIT_RadiusFromCPObject</seealso>
    class RadiusFromCPObject : ParticleFunctionInitializer
    {
        private readonly int controlPoint;

        public RadiusFromCPObject(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
        }

        public override ulong WrittenFields => ParticleField.Radius.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            FillRadii(run, particleSystemState);

            // An unused random draw per particle
            for (var i = 0; i < run.Length; i++)
            {
                _ = particleSystemState.Random.Next();
            }
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            FillRadii(run, particleSystemState);

            // An unused draw from the thread generator per block of four
            foreach (var block in run.Blocks())
            {
                particleSystemState.ThreadRandom.Next();
            }
        }

        private void FillRadii(ParticleSpan run, ParticleSystemState particleSystemState)
        {
            var scale = particleSystemState.GetControlPoint(controlPoint).ResolveModelObject()?.Transform.MaxAxisScale() ?? 1f;
            var strength = GetOperatorRunStrength(particleSystemState);

            run.Floats(ParticleField.Radius).Fill(scale * strength);
        }
    }
}
