namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Normalizes a vector particle attribute to unit length, then multiplies the result by a
    /// scale factor.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_NormalizeVector">C_OP_NormalizeVector</seealso>
    class NormalizeVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly float scale = 1.0f;

        public NormalizeVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            scale = parse.Float("m_flScale", scale);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var vector = particle.GetVector(outputField);

                // A zero vector has no direction to normalize
                if (vector == Vector3.Zero)
                {
                    continue;
                }

                vector = Vector3.Normalize(vector) * scale;

                particle.SetVector(outputField, vector);
            }
        }
    }
}
