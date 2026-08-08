using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Moves particles by however far the path they were spawned on travelled since the previous
    /// frame, reading the path parameter and segment control points that
    /// <see cref="Initializers.CreateSequentialPathV2"/> saved to the hitbox offset attribute. The
    /// segment is taken from the saved control points instead of a dense segment index, and each
    /// path is evaluated once per frame the first time a particle references it.
    /// </summary>
    /// <remarks>
    /// <c>m_flFadeStart</c> and <c>m_flFadeEnd</c> are parsed but the engine never reads them, and
    /// operator strength is not applied either.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LockToSavedSequentialPathV2">C_OP_LockToSavedSequentialPathV2</seealso>
    class LockToSavedSequentialPathV2 : ParticleFunctionOperator
    {
        private const int ControlPointCount = 64;

        private readonly bool cpPairs;
        private readonly ParticlePathParameters pathParams;

        private readonly (Vector3 Start, Vector3 Mid, Vector3 End)[] currentPaths = new (Vector3, Vector3, Vector3)[ControlPointCount];
        private readonly (Vector3 Start, Vector3 Mid, Vector3 End)?[] previousPaths = new (Vector3, Vector3, Vector3)?[ControlPointCount];
        private readonly bool[] evaluatedThisFrame = new bool[ControlPointCount];

        public LockToSavedSequentialPathV2(ParticleDefinitionParser parse) : base(parse)
        {
            _ = parse.Float("m_flFadeStart", 1f);
            _ = parse.Float("m_flFadeEnd", 1f);
            cpPairs = parse.Boolean("m_bCPPairs", cpPairs);

            var parsed = new ParticlePathParameters(parse);
            pathParams = parsed.WithControlPoints(
                Math.Clamp(parsed.StartControlPointNumber, 0, ControlPointCount - 1),
                Math.Clamp(parsed.EndControlPointNumber, 0, ControlPointCount - 1));

            if (Math.Abs(pathParams.EndControlPointNumber - pathParams.StartControlPointNumber) <= 1)
            {
                cpPairs = false;
            }
        }

        public override void Reset()
        {
            Array.Clear(previousPaths);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            Array.Clear(evaluatedThisFrame);

            foreach (ref var particle in particles.Current)
            {
                var saved = particle.HitboxOffsetPosition;
                var startControlPoint = Math.Clamp((int)saved.Y, 0, ControlPointCount - 1);

                if (!evaluatedThisFrame[startControlPoint])
                {
                    var endControlPoint = cpPairs ? (int)saved.Z : pathParams.EndControlPointNumber;
                    var segment = pathParams.WithControlPoints(startControlPoint, endControlPoint);

                    currentPaths[startControlPoint] = ParticlePath.CalculatePathValues(particleSystemState, segment, particleSystemState.Age);
                    previousPaths[startControlPoint] ??= ParticlePath.CalculatePathValues(particleSystemState, segment, particleSystemState.Age - frameTime);
                    evaluatedThisFrame[startControlPoint] = true;
                }

                var current = currentPaths[startControlPoint];
                var previous = previousPaths[startControlPoint]!.Value;

                var creationFraction = frameTime > 0f
                    ? MathF.Min(particle.Age, frameTime) / frameTime
                    : 1f;

                var delta = (ParticlePath.Evaluate(current.Start, current.Mid, current.End, saved.X)
                    - ParticlePath.Evaluate(previous.Start, previous.Mid, previous.End, saved.X)) * creationFraction;

                particle.Position += delta;
                particle.PositionPrevious += delta;
            }

            for (var i = 0; i < ControlPointCount; i++)
            {
                previousPaths[i] = evaluatedThisFrame[i] ? currentPaths[i] : null;
            }
        }
    }
}
