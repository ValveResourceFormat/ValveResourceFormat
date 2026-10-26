using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Moves particles bound to bones with the bones of a control point's model: every step each
    /// particle is carried by how its blend of bone transforms changed since the previous step, at
    /// full weight until a fade window over its life. A control point jump moves particles fully.
    /// Normals and radii can follow the bones too.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SnapshotSkinToBones">C_OP_SnapshotSkinToBones</seealso>
    class SnapshotSkinToBones : ParticleFunctionOperator
    {
        private readonly bool transformNormals;
        private readonly bool transformRadii;
        private readonly int controlPoint;
        private readonly float lifeTimeFadeStart = 1f;
        private readonly float lifeTimeFadeEnd = 1f;
        private readonly float jumpThreshold = 100f;
        private readonly float prevPosScale = 1f;

        public SnapshotSkinToBones(ParticleDefinitionParser parse) : base(parse)
        {
            transformNormals = parse.Boolean("m_bTransformNormals", transformNormals);
            transformRadii = parse.Boolean("m_bTransformRadii", transformRadii);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            lifeTimeFadeStart = parse.Float("m_flLifeTimeFadeStart", lifeTimeFadeStart);
            lifeTimeFadeEnd = parse.Float("m_flLifeTimeFadeEnd", lifeTimeFadeEnd);
            jumpThreshold = parse.Float("m_flJumpThreshold", jumpThreshold);
            jumpThreshold *= jumpThreshold;
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
        }

        /// <inheritdoc/>
        public override ulong StoredFields
            => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Radius.Mask() | ParticleField.Normal.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true) is not { IsStable: true } cache
                || !particles.Current.HasBones)
            {
                return;
            }

            var point = particleSystemState.GetControlPoint(controlPoint);
            var jumped = Vector3.DistanceSquared(point.Position, point.PositionPrevious) > jumpThreshold;
            var fadeEnd = lifeTimeFadeEnd <= 0f ? ParticleMath.NoFadeEnd : lifeTimeFadeEnd;
            var inverseFade = lifeTimeFadeEnd > lifeTimeFadeStart ? 1f / (lifeTimeFadeEnd - lifeTimeFadeStart) : 0f;
            var prevScale = jumped ? 1f : prevPosScale;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var ages = current.ReadAges();
            var boneWeights = current.ReadVectors(ParticleField.BoneWeights);
            var boneIndices = current.BoneIndices;

            var normals = transformNormals ? current.Vectors(ParticleField.Normal) : default;
            var radii = transformRadii ? current.Floats(ParticleField.Radius) : default;

            for (var i = 0; i < positions.Length; i++)
            {
                var age = ages[i];
                var weight = frameTime >= age ? 0f : 1f;

                if (!jumped && age >= fadeEnd)
                {
                    continue;
                }

                if (!cache.ComputeSkinningMatrix(boneIndices[i], boneWeights[i], true, out var skin, out var previousSkin)
                    || !Matrix4x4.Invert(previousSkin, out var previousInverse))
                {
                    continue;
                }

                var delta = previousInverse * skin;

                if (!jumped && age > lifeTimeFadeStart)
                {
                    weight *= (1f - ((age - lifeTimeFadeStart) * inverseFade)) * strength;
                }

                var position = positions[i];
                var previousPosition = previousPositions[i];
                positions[i] = MathUtils.Lerp(position, Vector3.Transform(position, delta), weight);
                previousPositions[i] = MathUtils.Lerp(previousPosition, Vector3.Transform(previousPosition, delta), weight * prevScale);

                if (transformNormals)
                {
                    normals[i] = MathUtils.SafeNormalize(Vector3.TransformNormal(normals[i], delta));
                }

                if (transformRadii)
                {
                    radii[i] *= delta.MaxAxisScale();
                }
            }
        }
    }
}
