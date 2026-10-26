namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes a single component (X, Y, or Z) of a vector particle attribute to a random scalar value between a min and max.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomVectorComponent">C_INIT_RandomVectorComponent</seealso>
    class RandomVectorComponent : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float min;
        private readonly float max;
        private readonly int component;

        public RandomVectorComponent(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            min = parse.Float("m_flMin", min);
            max = parse.Float("m_flMax", max);
            component = Math.Clamp(parse.Int32("m_nComponent", component), 0, 2);
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var isVector = ParticleBuffer.IsVector(fieldOutput);
            var vectors = isVector ? run.Vectors(fieldOutput) : default;

            for (var i = 0; i < run.Length; i++)
            {
                var newComponent = particleSystemState.Random.NextBetween(min, max);

                // A scalar field is indexed as a vector too, which spills components 1 and 2 onto the particles 4 and 8
                // places ahead; scalar fields are not written here
                if (isVector)
                {
                    vectors[i][component] = newComponent;
                }
            }
        }
    }
}
