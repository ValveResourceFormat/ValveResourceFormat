namespace ValveResourceFormat.Renderer.Particles.Operators
{
    // seriously?
    class RemapCrossProductOfTwoVectorsToVector : ParticleFunctionOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly IVectorProvider inputVec1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider inputVec2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly bool normalize;

        public RemapCrossProductOfTwoVectorsToVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            inputVec1 = parse.VectorProvider("m_InputVec1", inputVec1);
            inputVec2 = parse.VectorProvider("m_InputVec2", inputVec2);
            normalize = parse.Boolean("m_bNormalize", normalize);
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var vec1 = inputVec1.NextVector(ref particle, particleSystemState);
                var vec2 = inputVec2.NextVector(ref particle, particleSystemState);

                var cross = Vector3.Cross(vec1, vec2);

                if (normalize)
                {
                    cross = Vector3.Normalize(cross);
                }

                particle.SetVector(FieldOutput, cross);
            }
        }
    }
}
