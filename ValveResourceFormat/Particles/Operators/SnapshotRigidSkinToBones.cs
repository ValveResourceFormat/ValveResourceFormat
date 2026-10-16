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
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, ParticleSystemState.MaxControlPoint);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true) is not { Count: > 0 } cache)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                if (!cache.ComputeSkinningMatrix(particle.BoneIndices, particle.BoneWeights, false, out var skin, out _))
                {
                    continue;
                }

                ref var initial = ref particles.Initial[particle.Index];
                var rest = particle.HitboxOffsetPosition;

                particle.Position = Vector3.Transform(rest, skin);
                particle.PositionPrevious = Vector3.Transform(rest + initial.PositionPrevious, skin);

                if (transformNormals)
                {
                    particle.Normal = MathUtils.SafeNormalize(Vector3.TransformNormal(initial.Normal, skin));
                }

                if (transformRadii)
                {
                    particle.Radius = skin.MaxAxisScale() * initial.Radius;
                }
            }
        }
    }
}
