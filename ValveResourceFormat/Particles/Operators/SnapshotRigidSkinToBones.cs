using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Re-skins particles bound to bones to the current pose of a control point's model every step:
    /// the rest position recorded at creation, offset by the initial previous position, is carried
    /// through the particle's blend of bone transforms. Normals and radii can follow the bones too.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SnapshotRigidSkinToBones">C_OP_SnapshotRigidSkinToBones</seealso>
    class SnapshotRigidSkinToBones : ParticleFunctionOperator
    {
        private readonly bool transformNormals;
        private readonly bool transformRadii;
        private readonly int controlPoint;

        public SnapshotRigidSkinToBones(ParticleDefinitionParser parse) : base(parse)
        {
            transformNormals = parse.Boolean("m_bTransformNormals", transformNormals);
            transformRadii = parse.Boolean("m_bTransformRadii", transformRadii);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);

            parse.AddInitialRead(ParticleField.Position);
            parse.AddInitialRead(ParticleField.PositionPrevious);
            parse.AddInitialRead(ParticleField.Radius);
            parse.AddInitialRead(ParticleField.Normal);
        }

        /// <inheritdoc/>
        public override ulong StoredFields
            => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Radius.Mask() | ParticleField.Normal.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true) is not { Count: > 0 } cache
                || !particles.Current.HasBones)
            {
                return;
            }

            var current = particles.Current;
            var initial = particles.Initial;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var initialPreviousPositions = initial.ReadVectors(ParticleField.PositionPrevious);
            var restPositions = current.ReadVectors(ParticleField.HitboxOffsetPosition);
            var boneWeights = current.ReadVectors(ParticleField.BoneWeights);
            var boneIndices = current.BoneIndices;

            var normals = transformNormals ? current.Vectors(ParticleField.Normal) : default;
            var initialNormals = transformNormals ? initial.ReadVectors(ParticleField.Normal) : default;
            var radii = transformRadii ? current.Floats(ParticleField.Radius) : default;
            var initialRadii = transformRadii ? initial.ReadFloats(ParticleField.Radius) : default;

            for (var i = 0; i < positions.Length; i++)
            {
                if (!cache.ComputeSkinningMatrix(boneIndices[i], boneWeights[i], false, out var skin, out _))
                {
                    continue;
                }

                var rest = restPositions[i];

                positions[i] = Vector3.Transform(rest, skin);
                previousPositions[i] = Vector3.Transform(rest + initialPreviousPositions[i], skin);

                if (transformNormals)
                {
                    normals[i] = MathUtils.SafeNormalize(Vector3.TransformNormal(initialNormals[i], skin));
                }

                if (transformRadii)
                {
                    radii[i] = skin.MaxAxisScale() * initialRadii[i];
                }
            }
        }
    }
}
