using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Moves particles by however far the path they were spawned on travelled since the previous
    /// frame, reading the path parameter and segment control points that
    /// <see cref="Initializers.CreateSequentialPath"/> saved to the hitbox offset attribute.
    /// Particles keep whatever offset they have from the path rather than being snapped back onto it.
    /// </summary>
    /// <remarks>
    /// <c>m_flFadeStart</c> and <c>m_flFadeEnd</c> are parsed but the engine never reads them, and
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
            _ = parse.Float("m_flFadeStart", 1f);
            _ = parse.Float("m_flFadeEnd", 1f);
            cpPairs = parse.Boolean("m_bCPPairs", cpPairs);

            var parsed = new ParticlePathParameters(parse);
            pathParams = parsed.WithControlPoints(
                Math.Clamp(parsed.StartControlPointNumber, 0, 63),
                Math.Clamp(parsed.EndControlPointNumber, 0, 63));

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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
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

            foreach (ref var particle in particles.Current)
            {
                var saved = particle.HitboxOffsetPosition;
                var segmentIndex = Math.Clamp((int)(saved.Y - pathParams.StartControlPointNumber), 0, pathCount - 1);

                var current = currentPaths[segmentIndex];
                var previous = previousPaths[segmentIndex]!.Value;

                var creationFraction = frameTime > 0f
                    ? MathF.Min(particle.Age, frameTime) / frameTime
                    : 1f;

                var delta = (ParticlePath.Evaluate(current.Start, current.Mid, current.End, saved.X)
                    - ParticlePath.Evaluate(previous.Start, previous.Mid, previous.End, saved.X)) * creationFraction;

                particle.Position += delta;
                particle.PositionPrevious += delta;
            }

            for (var i = 0; i < pathCount; i++)
            {
                previousPaths[i] = currentPaths[i];
            }
        }
    }
}
