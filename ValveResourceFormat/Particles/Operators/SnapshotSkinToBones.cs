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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true) is not { IsStable: true } cache)
            {
                return;
            }

            var point = particleSystemState.GetControlPoint(controlPoint);
            var jumped = Vector3.DistanceSquared(point.Position, point.PositionPrevious) > jumpThreshold;
            var fadeEnd = lifeTimeFadeEnd <= 0f ? 1e20f : lifeTimeFadeEnd;
            var inverseFade = lifeTimeFadeEnd > lifeTimeFadeStart ? 1f / (lifeTimeFadeEnd - lifeTimeFadeStart) : 0f;
            var prevScale = jumped ? 1f : prevPosScale;
            var time = particleSystemState.Age;

            foreach (ref var particle in particles.Current)
            {
                var age = time - particle.CreationTime;
                var weight = frameTime >= age ? 0f : 1f;

                if (!jumped && age >= fadeEnd)
                {
                    continue;
                }

                if (!cache.ComputeSkinningMatrix(particle.BoneIndices, particle.BoneWeights, true, out var skin, out var previousSkin)
                    || !Matrix4x4.Invert(previousSkin, out var previousInverse))
                {
                    continue;
                }

                var delta = previousInverse * skin;

                if (!jumped && age > lifeTimeFadeStart)
                {
                    weight *= (1f - ((age - lifeTimeFadeStart) * inverseFade)) * strength;
                }

                particle.Position = MathUtils.Lerp(particle.Position, Vector3.Transform(particle.Position, delta), weight);
                particle.PositionPrevious = MathUtils.Lerp(particle.PositionPrevious, Vector3.Transform(particle.PositionPrevious, delta), weight * prevScale);

                if (transformNormals)
                {
                    particle.Normal = MathUtils.SafeNormalize(Vector3.TransformNormal(particle.Normal, delta));
                }

                if (transformRadii)
                {
                    particle.Radius *= delta.MaxAxisScale();
                }
            }
        }
    }
}
