using ValveResourceFormat.Particles.Utils;

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

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // Gravity is set up before the drag is evaluated and read after it, so only an input drawing per block
            // draws after the drag
            var setUpGravity = gravity.DrawsPerBlock ? Vector3.Zero : gravity.NextVector(particleSystemState);

            var timeStepSquared = frameTime * frameTime;
            var dragValue = Math.Clamp(drag.NextNumber(particleSystemState), 0f, 1f - ParticleMath.FloatEpsilon);
            var stepRatio = particleSystemState.PreviousStep > 0f ? frameTime / particleSystemState.PreviousStep : 1f;
            var dragFactor = stepRatio * MathF.Pow(1f - dragValue, frameTime * 30f);

            var gravityAcceleration = gravity.DrawsPerBlock ? gravity.NextVector(particleSystemState) : setUpGravity;

            var current = particles.Current;
            var forces = ParticleScratch.Vectors(current.Length);
            forces.Clear();

            // Force generators run from here and add into the accumulator before integration.
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

                    forceGenerator.MovementGravity = gravityAcceleration;
                    forceGenerator.GenerateForces(particles, forces, frameTime, particleSystemState, forceStrength);
                }
            }

            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var forceScales = current.ReadFloats(ParticleField.ForceScale);

            for (var i = 0; i < current.Length; i++)
            {
                // ForceScale (per-particle) weights the applied forces; 0 = pinned/immovable.
                var position = positions[i];
                var acceleration = gravityAcceleration + forces[i];
                var step = ((position - previousPositions[i]) * dragFactor) + (forceScales[i] * (timeStepSquared * acceleration));
                previousPositions[i] = position;
                positions[i] = position + step;
            }

            particleSystemState.Data?.RunConstraints(particles, frameTime, maxConstraintPasses);
        }
    }
}
