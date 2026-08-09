using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The path definition shared by the path-based initializers and operators
    /// (<c>m_PathParams</c> in the schema): a control point pair with endpoint offsets, a midpoint
    /// bias, and a bulge that displaces the midpoint.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CPathParameters">CPathParameters</seealso>
    readonly struct ParticlePathParameters
    {
        public readonly int StartControlPointNumber;
        public readonly int EndControlPointNumber;
        public readonly int BulgeControl;
        public readonly float Bulge;
        public readonly float MidPoint = 0.5f;
        public readonly Vector3 StartPointOffset = Vector3.Zero;
        public readonly Vector3 MidPointOffset = Vector3.Zero;
        public readonly Vector3 EndOffset = Vector3.Zero;

        public ParticlePathParameters(int startControlPointNumber, int endControlPointNumber)
        {
            StartControlPointNumber = startControlPointNumber;
            EndControlPointNumber = endControlPointNumber;
        }

        public ParticlePathParameters(ParticleDefinitionParser parse)
        {
            var pathParams = parse.Data.GetSubCollection("m_PathParams");
            if (pathParams == null)
            {
                return;
            }

            parse = parse.Nested(pathParams);

            StartControlPointNumber = parse.Int32("m_nStartControlPointNumber", StartControlPointNumber);
            EndControlPointNumber = parse.Int32("m_nEndControlPointNumber", EndControlPointNumber);
            BulgeControl = parse.Int32("m_nBulgeControl", BulgeControl);
            Bulge = parse.Float("m_flBulge", Bulge);
            MidPoint = parse.Float("m_flMidPoint", MidPoint);
            StartPointOffset = parse.Vector3("m_vStartPointOffset", StartPointOffset);
            MidPointOffset = parse.Vector3("m_vMidPointOffset", MidPointOffset);
            EndOffset = parse.Vector3("m_vEndOffset", EndOffset);
        }

        /// <summary>This path with a different control point pair, keeping every other parameter.</summary>
        public ParticlePathParameters WithControlPoints(int start, int end)
            => new(start, end, this);

        private ParticlePathParameters(int start, int end, in ParticlePathParameters source)
        {
            StartControlPointNumber = start;
            EndControlPointNumber = end;
            BulgeControl = source.BulgeControl;
            Bulge = source.Bulge;
            MidPoint = source.MidPoint;
            StartPointOffset = source.StartPointOffset;
            MidPointOffset = source.MidPointOffset;
            EndOffset = source.EndOffset;
        }
    }

    /// <summary>
    /// Evaluates path positions the way the engine does: three points (start, displaced midpoint,
    /// end) form a quadratic Bezier, so the curve bends toward the midpoint without passing
    /// through it.
    /// </summary>
    static class ParticlePath
    {
        /// <summary>
        /// A control point's position at a time within the last frame, interpolated across the
        /// recorded control point history.
        /// </summary>
        public static Vector3 GetControlPointAtTime(ParticleSystemRenderState state, int controlPoint, float time)
        {
            var point = state.GetControlPoint(controlPoint);
            var stepTime = point.PreviousStepTime;

            if (stepTime <= 0f)
            {
                return point.Position;
            }

            var fraction = MathF.Max((stepTime - (state.Age - time)) / stepTime, 0f);
            return Vector3.Lerp(point.PositionPrevious, point.Position, fraction);
        }

        /// <summary>
        /// Computes the Bezier control triple for a path at the given timestamp. Endpoint offsets
        /// apply before the midpoint is derived; the bulge displaces the midpoint along the bulge
        /// control point's forward direction scaled by how perpendicular that direction is to the
        /// path; without a bulge control point the displacement is instead drawn per world axis in
        /// <c>[-bulge, +bulge)</c> from the system's random seed alone, so it stays fixed for the
        /// lifetime of the system instance; the midpoint offset applies last.
        /// </summary>
        public static (Vector3 Start, Vector3 Mid, Vector3 End) CalculatePathValues(
            ParticleSystemRenderState state, in ParticlePathParameters path, float timeStamp)
        {
            var start = GetControlPointAtTime(state, path.StartControlPointNumber, timeStamp) + path.StartPointOffset;
            var end = GetControlPointAtTime(state, path.EndControlPointNumber, timeStamp) + path.EndOffset;

            var mid = Vector3.Lerp(start, end, path.MidPoint);

            if (path.BulgeControl != 0)
            {
                var bulgeCp = path.BulgeControl == 2 ? path.EndControlPointNumber : path.StartControlPointNumber;
                var forward = state.GetControlPoint(bulgeCp).Orientation;
                var direction = end - start;
                var length = direction.Length();

                var perpendicularity = length > Epsilon.Length
                    ? 1f - MathF.Abs(Vector3.Dot(direction / length, forward))
                    : 0f;

                var forwardLength = forward.Length();
                if (forwardLength > Epsilon.Length)
                {
                    mid += forward * (length * path.Bulge * perpendicularity / forwardLength);
                }
            }
            else
            {
                mid += ParticleRandom.ForSampleBetweenPerComponent(state.Random.Seed, new Vector3(-path.Bulge), new Vector3(path.Bulge));
            }

            mid += path.MidPointOffset;

            return (start, mid, end);
        }

        /// <summary>The quadratic Bezier through the control triple at parameter <paramref name="t"/>.</summary>
        public static Vector3 Evaluate(Vector3 start, Vector3 mid, Vector3 end, float t)
            => Vector3.Lerp(Vector3.Lerp(start, mid, t), Vector3.Lerp(mid, end, t), t);
    }
}
