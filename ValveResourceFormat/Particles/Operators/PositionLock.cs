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

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public PositionLock(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            startTimeExp = ParticleMath.TruncateToQuarterSteps(parse.Float("m_flStartTime_exp", startTimeExp));
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            endTimeExp = ParticleMath.TruncateToQuarterSteps(parse.Float("m_flEndTime_exp", endTimeExp));
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

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask() | outputFieldPrev.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // Compute the transform delta once per frame; per particle, only the first would see it move
            var transform = transformInput.NextTransform(particleSystemState);
            var transformPosition = transform.Translation;
            var scale = componentScale.NextVector(particleSystemState);

            if (previousTransformPosition.X == float.MaxValue)
            {
                previousTransform = transformInput.NextTransformAtTime(particleSystemState, particleSystemState.StepStartAge);
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

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var positions = current.Vectors(outputField);
            var movesPrevious = prevPosScale != 0f && ParticleBuffer.IsVector(outputFieldPrev);
            var previousPositions = movesPrevious ? current.Vectors(outputFieldPrev) : default;

            var windows = alwaysLocked ? default : FillWindows(particleSystemState.ThreadRandom, ages, lifetimes, instantJump);
            var rotatesWholeBlocks = fadeRange != 0f && lockRotation;

            for (var i = 0; i < positions.Length; i++)
            {
                var window = 1f;

                if (!alwaysLocked)
                {
                    window = windows[i];

                    if (IsUnlocked(windows, i, rotatesWholeBlocks))
                    {
                        continue;
                    }
                }

                var currentPosition = positions[i];

                var creationFraction = MathF.Min(ages[i], frameTime) / frameTime;

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

                    positions[i] = currentPosition + ((Vector3.Transform(currentPosition, rotationLock) - currentPosition) * weight);

                    if (movesPrevious)
                    {
                        var currentPrevious = previousPositions[i];
                        previousPositions[i] = currentPrevious + ((Vector3.Transform(currentPrevious, rotationLock) - currentPrevious) * weight * prevPosScale);
                    }
                }
                else
                {
                    // Translate the particle with the transform, preserving its offset from it
                    var scaledMove = move * scale;

                    positions[i] = currentPosition + scaledMove;

                    if (movesPrevious)
                    {
                        previousPositions[i] += scaledMove * prevPosScale;
                    }
                }
            }
        }

        /// <summary>
        /// Works out how strongly each particle is still locked, 1 before its unlock window and 0 after
        /// it. The window's start and end are redrawn every step from the thread generator, one pair per
        /// block of four particles with a value per particle, so the window moves under a particle while
        /// it is alive. An instant jump locks every particle fully, though the draws are still taken.
        /// </summary>
        private Span<float> FillWindows(ThreadRandom generator, ReadOnlySpan<float> ages, ReadOnlySpan<float> lifetimes, bool instantJump)
        {
            var windows = ParticleScratch.Floats(ages.Length);

            for (var blockStart = 0; blockStart < windows.Length; blockStart += 4)
            {
                var startDraws = generator.Next();
                var endDraws = generator.Next();
                var blockEnd = Math.Min(blockStart + 4, windows.Length);

                for (var i = blockStart; i < blockEnd; i++)
                {
                    var lane = i - blockStart;
                    var startTime = MathUtils.Lerp(startTimeMin, startTimeMax, MathF.Pow(startDraws[lane], startTimeExp));
                    var endTime = MathUtils.Lerp(endTimeMin, endTimeMax, MathF.Pow(endDraws[lane], endTimeExp));

                    var lifeFraction = ParticleMath.ClampToUnit(ages[i] / lifetimes[i]);
                    windows[i] = instantJump ? 1f : 1f - ParticleMath.ClampToUnit((lifeFraction - startTime) / (endTime - startTime));
                }
            }

            return windows;
        }

        /// <summary>
        /// Whether the particle at <paramref name="index"/> is left alone because its window has closed.
        /// When <paramref name="rotatesWholeBlocks"/>, it is still rotated at full weight while another
        /// particle of its block of four is locked.
        /// </summary>
        private static bool IsUnlocked(ReadOnlySpan<float> windows, int index, bool rotatesWholeBlocks)
        {
            if (windows[index] > 0f)
            {
                return false;
            }

            if (!rotatesWholeBlocks)
            {
                return true;
            }

            var blockStart = index & ~3;
            var blockEnd = Math.Min(blockStart + 4, windows.Length);

            for (var i = blockStart; i < blockEnd; i++)
            {
                if (windows[i] > 0f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
