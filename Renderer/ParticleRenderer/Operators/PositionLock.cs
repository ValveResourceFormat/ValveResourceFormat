using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
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
            startTimeExp = Math.Clamp(parse.Float("m_flStartTime_exp", startTimeExp), -255f, 255f);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            endTimeExp = Math.Clamp(parse.Float("m_flEndTime_exp", endTimeExp), -255f, 255f);
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            // The transform delta must be computed once per frame, not per particle,
            // otherwise only the first particle ever observes the transform moving
            var transform = transformInput.NextTransform(particleSystemState);
            var transformPosition = transform.Translation;
            var scale = componentScale.NextVector(particleSystemState);

            // Also per frame: the transform does not vary per particle, so neither does its direction
            var hasOrientation = transformInput.TryGetOrientation(ref Particle.Default, particleSystemState, out var lockedNormal);

            if (previousTransformPosition.X == float.MaxValue)
            {
                previousTransformPosition = transformPosition;
                previousTransform = transform;
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

            // A jump beyond the threshold teleports particles with the transform at full strength;
            // the squared delta is tested against the raw threshold value
            var instantJump = instantJumpThreshold != 0f && delta.LengthSquared() > instantJumpThreshold;

            if (delta == Vector3.Zero && !lockRotation)
            {
                return;
            }

            // A start fadeout of 1 (the default) means the lock never fades and is applied every frame
            var alwaysLocked = startTimeMin >= 1f;

            // The range bias is a collection-scope input, evaluated once for the whole frame
            var bias = rangeBias.NextNumber(particleSystemState);

            foreach (ref var particle in particles.Current)
            {
                var timeFade = 1f;

                if (!alwaysLocked)
                {
                    var startTime = ParticleRandom.ForSampleWithExponentBetween(particle.ParticleId, startTimeExp, startTimeMin, startTimeMax);
                    var endTime = ParticleRandom.ForSampleWithExponentBetween(particle.ParticleId, endTimeExp, endTimeMin, endTimeMax);

                    // Fully locked until startTime, fading the lock out until endTime, using normalized lifetime
                    timeFade = particle.NormalizedAge <= startTime
                        ? 1f
                        : 1f - MathUtils.Saturate(MathUtils.Remap(particle.NormalizedAge, startTime, endTime));

                    if (instantJump)
                    {
                        timeFade = 1f;
                    }
                }

                var lockStrength = strength * timeFade;

                if (lockStrength <= 0f)
                {
                    continue;
                }

                var currentPosition = particle.GetVector(outputField);

                // A particle born partway through the step only travelled with the transform for the
                // part of the step it existed for
                var creationFraction = frameTime > 0f
                    ? MathF.Min(particle.Age, frameTime) / frameTime
                    : 1f;

                var scaledDelta = delta * scale * (creationFraction * lockStrength);

                // The always-locked loop weights the rotation by operator strength where the fading one
                // weights it by the time fade alone
                var rotationLockStrength = alwaysLocked ? strength : timeFade;

                if (fadeRange > 0f)
                {
                    // Distance is measured against the position the particle is about to occupy
                    var distance = Vector3.Distance(transformPosition, currentPosition + scaledDelta);
                    var normalizedDistance = MathUtils.Saturate(distance / fadeRange);
                    var fade = NumericBias.Standard(normalizedDistance, bias);

                    scaledDelta *= 1f - fade;
                    rotationLockStrength = 1f - (fade * rotationLockStrength);
                }

                if (lockRotation)
                {
                    // Move both positions by the transform delta so particles follow the control point's rotation
                    var currentPrevious = particle.GetVector(outputFieldPrev);
                    var rotatedPosition = Vector3.Transform(currentPosition, rotationLock);
                    var rotatedPrevious = Vector3.Transform(currentPrevious, rotationLock);

                    var rotationStrength = rotationLockStrength * creationFraction;

                    particle.SetVector(outputFieldPrev, currentPrevious + (rotatedPrevious - currentPrevious) * (rotationStrength * prevPosScale));
                    particle.SetVector(outputField, currentPosition + (rotatedPosition - currentPosition) * rotationStrength);
                    // A transform with no orientation of its own has no direction to hand the normal,
                    // so the particle keeps the one it had
                    if (hasOrientation)
                    {
                        particle.Normal = lockedNormal;
                    }
                }
                else
                {
                    // Translate the particle with the transform, preserving its offset from it
                    particle.SetVector(outputFieldPrev, particle.GetVector(outputFieldPrev) + (scaledDelta * prevPosScale));
                    particle.SetVector(outputField, currentPosition + scaledDelta);
                }
            }
        }
    }
}
