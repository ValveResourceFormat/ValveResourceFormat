namespace ValveResourceFormat.Renderer.Particles.Operators
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

        public BasicMovement(ParticleDefinitionParser parse) : base(parse)
        {
            gravity = parse.VectorProvider("m_Gravity", gravity);
            drag = parse.NumberProvider("m_fDrag", drag);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // The force generators run from inside this operator: it seeds a fresh
            // acceleration buffer with gravity and asks each generator to add to it before integrating.
            var forceGenerators = particleSystemState.Data?.ForceGenerators;
            if (forceGenerators != null)
            {
                foreach (var forceGenerator in forceGenerators)
                {
                    var strength = forceGenerator.GetOperatorRunStrength(particleSystemState);

                    if (strength <= 0.0f)
                    {
                        continue;
                    }

                    forceGenerator.GenerateForces(particles, frameTime, particleSystemState, strength);
                }
            }

            var timeStepSquared = frameTime * frameTime;
            var gravityMovement = gravity.NextVector(particleSystemState) * timeStepSquared;
            // Clamp drag to just under 1.
            var dragValue = Math.Clamp(drag.NextNumber(particleSystemState), 0.0f, 0.9999999f);
            var dragFactor = MathF.Exp(MathF.Log(1.0f - dragValue) / (1.0f / 30.0f) * frameTime);
            // Scale the inertia term by the current-to-previous step ratio so momentum stays
            // framerate-independent; the ratio is 1 whenever the step is fixed (pre-simulation) or steady.
            if (particles.PreviousFrameTime > 0f)
            {
                dragFactor *= frameTime / particles.PreviousFrameTime;
            }

            foreach (ref var particle in particles.Current)
            {
                // ForceScale (per-particle) weights the applied forces; 0 = pinned/immovable.
                var forces = gravityMovement + (particle.ForceAccumulator * timeStepSquared);
                var step = (particle.ForceScale * forces) + dragFactor * (particle.Position - particle.PositionPrevious);
                // Velocity is exposed in units per second so readers (MaxVelocity, RemapSpeed, providers)
                // compare against authored per-second values; it is (pos - prev)/dt.
                particle.Velocity = step / frameTime;
                particle.ForceAccumulator = Vector3.Zero;
                particle.PositionPrevious = particle.Position;
                particle.Position += step;
            }
        }
    }
}
