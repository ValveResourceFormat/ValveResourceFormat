namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Moves particles each frame by integrating velocity, applying a gravity vector and an exponential drag factor.
    /// </summary>
    /// <remarks>
    /// "Movement Basic" in the particle editor; "basic" in the sense of fundamental rather than
    /// simplistic: without it (or another movement operator) particles are spatially static.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_BasicMovement">C_OP_BasicMovement</seealso>
    class BasicMovement : ParticleFunctionOperator
    {
        private readonly IVectorProvider gravity = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider drag = new LiteralNumberProvider(0);
        private readonly int maxConstraintPasses = 3;

        public BasicMovement(ParticleDefinitionParser parse) : base(parse)
        {
            gravity = parse.VectorProvider("m_Gravity", gravity);
            drag = parse.NumberProvider("m_fDrag", drag);
            maxConstraintPasses = parse.Int32("m_nMaxConstraintPasses", maxConstraintPasses);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // Force generators run from here and add to ForceAccumulator before integration.
            var forceGenerators = particleSystemState.Data?.ForceGenerators;
            if (forceGenerators != null)
            {
                foreach (var forceGenerator in forceGenerators)
                {
                    var forceStrength = forceGenerator.GetOperatorRunStrength(particleSystemState);

                    if (forceStrength <= 0.0f)
                    {
                        continue;
                    }

                    forceGenerator.GenerateForces(particles, frameTime, particleSystemState, forceStrength);
                }
            }

            var timeStepSquared = frameTime * frameTime;
            var gravityAcceleration = gravity.NextVector(particleSystemState);
            var dragValue = Math.Clamp(drag.NextNumber(particleSystemState), 0.0f, 0.99999988f);
            var stepRatio = particles.PreviousFrameTime > 0f ? frameTime / particles.PreviousFrameTime : 1f;
            var dragFactor = stepRatio * MathF.Exp(MathF.Log(1.0f - dragValue) / (1.0f / 30.0f) * frameTime);

            foreach (ref var particle in particles.Current)
            {
                // ForceScale (per-particle) weights the applied forces; 0 = pinned/immovable.
                var acceleration = gravityAcceleration + particle.ForceAccumulator;
                var step = ((particle.Position - particle.PositionPrevious) * dragFactor) + (particle.ForceScale * (timeStepSquared * acceleration));
                particle.ForceAccumulator = Vector3.Zero;
                particle.PositionPrevious = particle.Position;
                particle.Position += step;
            }

            particleSystemState.Data?.RunConstraints(particles, frameTime, maxConstraintPasses);
        }
    }
}
