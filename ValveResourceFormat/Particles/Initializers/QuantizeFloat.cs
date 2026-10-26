namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Rounds a float attribute of each new particle to the nearest multiple of a per-particle step,
    /// taken in degrees for an angle. A step of 0 leaves the attribute alone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_QuantizeFloat">C_INIT_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionInitializer
    {
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0f);
        private readonly ParticleField outputField = ParticleField.Radius;

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
            outputField = parse.ParticleField("m_nOutputField", outputField);
        }

        public override ulong WrittenFields => outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var steps = inputValue.Fill(run, particleSystemState);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var values = run.Floats(outputField);
            var toRadians = outputField.IsOrientationField();

            for (var i = 0; i < values.Length; i++)
            {
                var step = steps[i];

                if (toRadians)
                {
                    step = float.DegreesToRadians(step);
                }

                if (step != 0f)
                {
                    values[i] = MathF.Floor((values[i] / step) + 0.5f) * step;
                }
            }
        }
    }
}
