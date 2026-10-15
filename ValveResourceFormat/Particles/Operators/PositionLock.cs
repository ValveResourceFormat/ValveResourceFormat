using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Causes particles to inherit the movement (and optionally rotation) of a transform input,
    /// optionally updating a previous position output field.
    /// </summary>
    /// <remarks>
    /// "Movement Lock to Control Point" in the particle editor.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PositionLock">C_OP_PositionLock</seealso>
    class PositionLock : ParticleFunctionOperator
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly float startTimeMin = 1f;
        private readonly float startTimeMax = 1f;
        private readonly float startTimeExp = 1f;
        private readonly float endTimeMin = 1f;
        private readonly float endTimeMax = 1f;
        private readonly float endTimeExp = 1f;
        private readonly float fadeRange;
        private readonly INumberProvider rangeBias = new LiteralNumberProvider(0.2f);
        private readonly float instantJumpThreshold = 512f;
        private readonly float prevPosScale = 1f;
        private readonly bool lockRotation;
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly ParticleField outputFieldPrev = ParticleField.PositionPrevious;

        private Vector3 previousTransformPosition = new(float.MaxValue);
        private Matrix4x4 previousTransform = Matrix4x4.Identity;

        public override void Reset()
        {
            previousTransformPosition = new Vector3(float.MaxValue);
            previousTransform = Matrix4x4.Identity;
        }

        public PositionLock(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            startTimeExp = QuarterStepExponent(parse.Float("m_flStartTime_exp", startTimeExp));
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            endTimeExp = QuarterStepExponent(parse.Float("m_flEndTime_exp", endTimeExp));
            fadeRange = parse.Float("m_flRange", fadeRange);
            rangeBias = parse.NumberProvider("m_flRangeBias", rangeBias);

            // The threshold is authored as a distance and squared once at load, so the per-frame
            // test can compare against a squared delta
            instantJumpThreshold = parse.Float("m_flJumpThreshold", instantJumpThreshold);
            instantJumpThreshold *= instantJumpThreshold;
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            lockRotation = parse.Boolean("m_bLockRot", lockRotation);
            componentScale = parse.VectorProvider("m_vecScale", componentScale);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputFieldPrev = parse.ParticleField("m_nFieldOutputPrev", outputFieldPrev);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // Compute the transform delta once per frame; per particle, only the first would see it move
            var transform = transformInput.NextTransform(particleSystemState);
            var transformPosition = transform.Translation;
            var scale = componentScale.NextVector(particleSystemState);

            if (previousTransformPosition.X == float.MaxValue)
            {
                previousTransform = transformInput.NextTransformAtTime(ref Particle.Default, particleSystemState, particleSystemState.FrameStartAge);
                previousTransformPosition = previousTransform.Translation;
            }

            var delta = transformPosition - previousTransformPosition;

            // When locking rotation, particles are moved by the change in the transform (current * inverse(previous))
            // so they orbit the control point; otherwise they are translated by the raw position delta
            var rotationLock = Matrix4x4.Identity;
            if (lockRotation && Matrix4x4.Invert(previousTransform, out var previousInverse))
            {
                rotationLock = previousInverse * transform;
            }

            previousTransformPosition = transformPosition;
            previousTransform = transform;

            // A jump beyond the threshold teleports particles with the transform at full strength.
            // Both sides of the test are squared distances, the threshold having been squared at load
            var instantJump = instantJumpThreshold != 0f && delta.LengthSquared() > instantJumpThreshold;

            // A start fadeout of 1 (the default) means the lock never fades and is applied every frame
            var alwaysLocked = startTimeMin >= 1f;

            // The range bias is a collection-scope input, evaluated once for the whole frame
            var bias = rangeBias.NextNumber(particleSystemState);

            foreach (ref var particle in particles.Current)
            {
                var window = 1f;

                if (!alwaysLocked)
                {
                    // Redrawn every frame from the running counter rather than fixed by the particle,
                    // so the window a particle unlocks over moves under it while it is alive
                    var startTime = particleSystemState.Random.NextWithExponentBetween(startTimeExp, startTimeMin, startTimeMax);
                    var endTime = particleSystemState.Random.NextWithExponentBetween(endTimeExp, endTimeMin, endTimeMax);

                    var lifeFraction = ClampToUnit(particle.Age / particle.Lifetime);
                    window = instantJump ? 1f : 1f - ClampToUnit((lifeFraction - startTime) / (endTime - startTime));

                    if (window <= 0f)
                    {
                        continue;
                    }
                }

                var currentPosition = particle.GetVector(outputField);

                // A particle born partway through the step only travelled with the transform for the
                // part of the step it existed for
                var creationFraction = MathF.Min(particle.Age, frameTime) / frameTime;

                var move = delta * (strength * creationFraction * window);
                var rotationWeight = window;

                if (fadeRange != 0f)
                {
                    // Distance is measured against the position the particle is about to occupy
                    var distance = Vector3.Distance(transformPosition, currentPosition + move);
                    var fade = ParticleMath.Bias(MathF.Min(1f, distance / fadeRange), bias);

                    move *= 1f - fade;
                    rotationWeight = 1f - (fade * window);
                }

                if (lockRotation)
                {
                    // Move both positions by the transform delta so particles follow the control point's rotation
                    var weight = scale * (rotationWeight * creationFraction);

                    particle.SetVector(outputField, currentPosition + ((Vector3.Transform(currentPosition, rotationLock) - currentPosition) * weight));

                    var currentPrevious = particle.GetVector(outputFieldPrev);
                    particle.SetVector(outputFieldPrev, currentPrevious + ((Vector3.Transform(currentPrevious, rotationLock) - currentPrevious) * weight * prevPosScale));
                }
                else
                {
                    // Translate the particle with the transform, preserving its offset from it
                    var scaledMove = move * scale;

                    particle.SetVector(outputField, currentPosition + scaledMove);
                    particle.SetVector(outputFieldPrev, particle.GetVector(outputFieldPrev) + (scaledMove * prevPosScale));
                }
            }
        }

        private static float QuarterStepExponent(float exponent) => MathF.Truncate(Math.Clamp(exponent, -255f, 255f) * 4f) / 4f;

        private static float ClampToUnit(float value) => value >= 0f ? MathF.Min(value, 1f) : 0f;
    }
}
