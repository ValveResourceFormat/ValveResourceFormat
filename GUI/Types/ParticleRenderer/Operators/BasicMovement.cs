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

        private static Vector3 GetVelocityFromPreviousPosition(Vector3 currPosition, Vector3 prevPosition)
        {
            var velocity = currPosition - prevPosition;
            return Vector3.Zero; // temp due to weird ordering
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var gravityMovement = gravity.NextVector(particleSystemState) * frameTime;

            foreach (ref var particle in particles.Current)
            {
                // SO. Velocity is partially computed using the previous frame's position.
                // Which means that anything with basicmovement will have additional effects that we
                // aren't accounting for if we don't apply whatever this is.

                // Apply acceleration
                particle.Velocity += gravityMovement;

                particle.Velocity += GetVelocityFromPreviousPosition(particle.Position, particle.PositionPrevious);

                // Apply drag
                // Is this right? this is a super important operator so it might be bad if this is wrong
                particle.Velocity *= 1 - (drag.NextNumber() * 60f * frameTime);

                // Velocity is only applied in MovementBasic. If you layer two MovementBasic's on top of one another it'll indeed apply velocity twice.
                particle.Position += particle.Velocity * frameTime;
            }
        }
    }
}
