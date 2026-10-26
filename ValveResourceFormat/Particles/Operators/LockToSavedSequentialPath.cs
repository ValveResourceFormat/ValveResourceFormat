using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Moves particles by however far the path they were spawned on travelled since the previous
    /// frame, reading the path parameter and segment control points that
    /// <see cref="Initializers.CreateSequentialPath"/> saved to the hitbox offset attribute.
    /// Particles keep whatever offset they have from the path rather than being snapped back onto it.
    /// </summary>
    /// <remarks>
    /// <c>m_flFadeStart</c> and <c>m_flFadeEnd</c> are not read, and
    /// operator strength is not applied either.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LockToSavedSequentialPath">C_OP_LockToSavedSequentialPath</seealso>
    class LockToSavedSequentialPath : ParticleFunctionOperator
    {
        private readonly bool cpPairs;
        private readonly ParticlePathParameters pathParams;
        private readonly int pathCount;

        private readonly (Vector3 Start, Vector3 Mid, Vector3 End)[] currentPaths;
        private readonly (Vector3 Start, Vector3 Mid, Vector3 End)?[] previousPaths;

        public LockToSavedSequentialPath(ParticleDefinitionParser parse) : base(parse)
        {
            cpPairs = parse.Boolean("m_bCPPairs", cpPairs);

            var parsed = new ParticlePathParameters(parse);
            pathParams = parsed.WithControlPoints(
                Math.Clamp(parsed.StartControlPointNumber, 0, ParticleSystemState.MaxControlPoint),
                Math.Clamp(parsed.EndControlPointNumber, 0, ParticleSystemState.MaxControlPoint));

            pathCount = cpPairs
                ? Math.Max(pathParams.EndControlPointNumber - pathParams.StartControlPointNumber, 1)
                : 1;

            currentPaths = new (Vector3, Vector3, Vector3)[pathCount];
            previousPaths = new (Vector3, Vector3, Vector3)?[pathCount];
        }

        public override void Reset()
        {
            Array.Clear(previousPaths);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            for (var i = 0; i < pathCount; i++)
            {
                var segment = pathParams;

                if (cpPairs)
                {
                    var segmentStart = pathParams.StartControlPointNumber + i;
                    segment = pathParams.WithControlPoints(segmentStart, segmentStart + 1);
                }

                currentPaths[i] = ParticlePath.CalculatePathValues(particleSystemState, segment, particleSystemState.Age);
                previousPaths[i] ??= ParticlePath.CalculatePathValues(particleSystemState, segment, particleSystemState.Age - frameTime);
            }

            var particleSpan = particles.Current;
            var positions = particleSpan.Vectors(ParticleField.Position);
            var previousPositions = particleSpan.Vectors(ParticleField.PositionPrevious);
            var savedValues = particleSpan.ReadVectors(ParticleField.HitboxOffsetPosition);
            var ages = particleSpan.ReadAges();

            for (var i = 0; i < positions.Length; i++)
            {
                var saved = savedValues[i];
                var segmentIndex = Math.Clamp((int)(saved.Y - pathParams.StartControlPointNumber), 0, pathCount - 1);

                var delta = PathDelta(currentPaths[segmentIndex], previousPaths[segmentIndex]!.Value, saved.X, ages[i], frameTime);

                positions[i] += delta;
                previousPositions[i] += delta;
            }

            for (var i = 0; i < pathCount; i++)
            {
                previousPaths[i] = currentPaths[i];
            }
        }

        /// <summary>
        /// How far the point at <paramref name="pathParameter"/> moved from the previous path to the current
        /// one, scaled down for a particle created during the step by the part of it that it lived.
        /// </summary>
        internal static Vector3 PathDelta((Vector3 Start, Vector3 Mid, Vector3 End) current, (Vector3 Start, Vector3 Mid, Vector3 End) previous,
            float pathParameter, float age, float frameTime)
        {
            var creationFraction = MathF.Min(age, frameTime) / frameTime;

            return (ParticlePath.Evaluate(current.Start, current.Mid, current.End, pathParameter)
                - ParticlePath.Evaluate(previous.Start, previous.Mid, previous.End, pathParameter)) * creationFraction;
        }
    }
}
