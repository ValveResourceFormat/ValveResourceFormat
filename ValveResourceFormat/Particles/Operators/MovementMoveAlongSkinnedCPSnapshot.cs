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
        private readonly ulong storedFields;

        public MovementMoveAlongSkinnedCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            snapshot = new SkinnedSnapshot(parse);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            setRadius = parse.Boolean("m_bSetRadius", setRadius);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            tValue = parse.NumberProvider("m_flTValue", tValue);

            storedFields = ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.HitboxOffsetPosition.Mask()
                | ParticleField.BoneIndices.Mask() | ParticleField.BoneWeights.Mask();

            if (setNormal)
            {
                storedFields |= ParticleField.Normal.Mask();
            }

            if (setRadius)
            {
                storedFields |= ParticleField.Radius.Mask();
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

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

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var normals = setNormal ? current.Vectors(ParticleField.Normal) : default;
            var radii = setRadius ? current.Floats(ParticleField.Radius) : default;
            var boneIndices = current.BoneIndices;
            var boneWeights = current.Vectors(ParticleField.BoneWeights);
            var hitboxOffsets = current.Vectors(ParticleField.HitboxOffsetPosition);

            var weights = interpolation.Fill(current, particleSystemState);
            var tValues = tValue.Fill(current, particleSystemState);

            for (var i = 0; i < positions.Length; i++)
            {
                var weight = weights[i];
                var t = (count - 1) * tValues[i];

                // The row index is kept in 16 bits, so a parameter far out of range wraps
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

                    boneIndices[i] = columns.BoneIndices(first, model, cache!.Count);
                    hitboxOffsets[i] = columns.Positions[first];
                    boneWeights[i] = columns.BoneWeights(first);

                    target = MathUtils.Lerp(firstPosition, secondPosition, blend);
                    normal = MathUtils.Lerp(firstNormal, secondNormal, blend);
                    radius = MathUtils.Lerp(firstRadius, secondRadius, blend);
                }

                positions[i] = MathUtils.Lerp(positions[i], target, weight);
                previousPositions[i] = MathUtils.Lerp(previousPositions[i], target, weight);

                if (setNormal)
                {
                    normals[i] = MathUtils.Lerp(normals[i], normal, weight);
                }

                if (setRadius)
                {
                    radii[i] = MathUtils.Lerp(radii[i], radius, weight);
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
