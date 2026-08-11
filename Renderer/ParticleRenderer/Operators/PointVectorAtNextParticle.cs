namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Lerps a vector particle attribute toward the unit direction from each particle to the next
    /// (or, with m_bPrevious, the previous) particle in the collection. The edge particle that has
    /// no neighbor in that direction copies its neighbor's resulting value instead.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PointVectorAtNextParticle">C_OP_PointVectorAtNextParticle</seealso>
    class PointVectorAtNextParticle : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);
        private readonly bool previous;

        public PointVectorAtNextParticle(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            previous = parse.Boolean("m_bPrevious", previous);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (particles.Count < 2)
            {
                return;
            }

            var current = particles.Current;
            var step = previous ? -1 : 1;
            var edge = previous ? 0 : particles.Count - 1;

            for (var i = 0; i < particles.Count; i++)
            {
                if (i == edge)
                {
                    continue;
                }

                ref var particle = ref current[i];
                var direction = current[i + step].Position - particle.Position;

                if (direction == Vector3.Zero)
                {
                    continue;
                }

                var interp = interpolation.NextNumber(ref particle, particleSystemState);
                var pointed = Vector3.Lerp(particle.GetVector(outputField), Vector3.Normalize(direction), interp);
                particle.SetVector(outputField, pointed);
            }

            current[edge].SetVector(outputField, current[edge - step].GetVector(outputField));
        }
    }
}
