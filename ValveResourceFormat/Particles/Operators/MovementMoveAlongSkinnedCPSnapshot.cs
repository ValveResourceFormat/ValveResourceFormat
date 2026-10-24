using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Pulls every particle each step toward the point a 0 to 1 parameter picks along a skinned
    /// control point snapshot, blending between neighbouring points deformed by the current pose of
    /// the bones of another control point's model. Without bones the points are taken as they are
    /// and not blended. Both the position and the previous position move, so a full pull leaves
    /// particles still.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementMoveAlongSkinnedCPSnapshot">C_OP_MovementMoveAlongSkinnedCPSnapshot</seealso>
    class MovementMoveAlongSkinnedCPSnapshot : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly SkinnedSnapshot snapshot;
        private readonly bool setNormal;
        private readonly bool setRadius;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);
        private readonly INumberProvider tValue = new LiteralNumberProvider(0f);

        public MovementMoveAlongSkinnedCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            snapshot = new SkinnedSnapshot(parse);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            setRadius = parse.Boolean("m_bSetRadius", setRadius);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            tValue = parse.NumberProvider("m_flTValue", tValue);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var cache = HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true);

            if (snapshot.Resolve(particleSystemState) is not { } columns
                || (setNormal && columns.Normals == null)
                || (setRadius && columns.Radii == null))
            {
                return;
            }

            var count = columns.Count;
            var model = cache is { Count: > 0 } ? cache.Object?.Model : null;

            foreach (var particle in particles.Current)
            {
                var weight = interpolation.NextNumber(particle, particleSystemState);
                var t = (count - 1) * tValue.NextNumber(particle, particleSystemState);
                var first = (int)(ushort)(int)MathF.Floor(t);
                var blend = t - first;

                if (first >= count)
                {
                    first = 0;
                }

                var second = Math.Min(first + 1, count - 1);

                Vector3 target;
                var normal = Vector3.Zero;
                var radius = 0f;

                if (model == null)
                {
                    target = columns.Positions[first];
                    normal = setNormal ? columns.Normals![first] : normal;
                    radius = setRadius ? columns.Radii![first] : radius;
                }
                else
                {
                    var (firstPosition, firstNormal, firstRadius) = Skin(cache!, columns, model, first);
                    var (secondPosition, secondNormal, secondRadius) = Skin(cache!, columns, model, second);

                    columns.Bind(particle, first, model, cache!.Count);

                    target = MathUtils.Lerp(firstPosition, secondPosition, blend);
                    normal = MathUtils.Lerp(firstNormal, secondNormal, blend);
                    radius = MathUtils.Lerp(firstRadius, secondRadius, blend);
                }

                particle.Position = MathUtils.Lerp(particle.Position, target, weight);
                particle.PositionPrevious = MathUtils.Lerp(particle.PositionPrevious, target, weight);

                if (setNormal)
                {
                    particle.Normal = MathUtils.Lerp(particle.Normal, normal, weight);
                }

                if (setRadius)
                {
                    particle.Radius = MathUtils.Lerp(particle.Radius, radius, weight);
                }
            }
        }

        private (Vector3 Position, Vector3 Normal, float Radius) Skin(HitBoxCache cache, SkinnedSnapshot.Columns columns, Model model, int row)
        {
            cache.ComputeSkinningMatrix(columns.BoneIndices(row, model, cache.Count), columns.BoneWeights(row), false, out var skin, out _);

            var position = Vector3.Transform(columns.Positions[row], skin);
            var normal = setNormal ? MathUtils.SafeNormalize(Vector3.TransformNormal(columns.Normals![row], skin)) : Vector3.Zero;
            var radius = setRadius ? skin.MaxAxisScale() * columns.Radii![row] : 0f;
            return (position, normal, radius);
        }
    }
}
