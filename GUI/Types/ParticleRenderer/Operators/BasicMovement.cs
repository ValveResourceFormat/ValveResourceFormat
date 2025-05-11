namespace GUI.Types.ParticleRenderer.Operators
{
    class BasicMovement : ParticleFunctionOperator
    {
        private readonly IVectorProvider gravity = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider drag = new LiteralNumberProvider(0);

        public BasicMovement(ParticleDefinitionParser parse) : base(parse)
        {
            gravity = parse.VectorProvider("m_Gravity", gravity);
            drag = parse.NumberProvider("m_fDrag", drag);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var gravityMovement = gravity.NextVector(particleSystemState) * (frameTime * frameTime);
            var dragValue = Math.Max(0.0f, drag.NextNumber(particleSystemState));
            var dragFactor = MathF.Exp(MathF.Log(1.0f - dragValue) / (1.0f / 30.0f) * frameTime);

            foreach (ref var particle in particles.Current)
            {
                particle.Velocity = gravityMovement + dragFactor * (particle.Position - particle.PositionPrevious);
                particle.PositionPrevious = particle.Position;
                particle.Position += particle.Velocity;
            }
        }
    }
}
