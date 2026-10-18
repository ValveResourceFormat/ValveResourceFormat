using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps particles on the front side of a plane, pushing any that sink behind it back out until
    /// their radius rests on it. The plane is placed in a control point's frame unless authored global.
    /// With a maximum distance, only particles closer than it to the plane's origin are held.
    /// </summary>
    /// <remarks>
    /// The old code (<c>m_bUseOldCode</c>, the default) handles particles in blocks of four and tests the
    /// maximum distance only to decide whether a block is held: once one particle in a block qualifies,
    /// every particle of the block behind the plane is pushed out, however far it is.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PlanarConstraint">C_OP_PlanarConstraint</seealso>
    class PlanarConstraint : ParticleFunctionConstraint
    {
        private readonly Vector3 pointOnPlane;
        private readonly Vector3 planeNormal = Vector3.UnitZ;
        private readonly int controlPoint;
        private readonly bool globalOrigin;
        private readonly bool globalNormal;
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider maximumDistanceToControlPoint = new LiteralNumberProvider(-1f);
        private readonly bool useOldCode = true;

        public PlanarConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            pointOnPlane = parse.Vector3("m_PointOnPlane", pointOnPlane);
            planeNormal = parse.Vector3("m_PlaneNormal", planeNormal);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            globalOrigin = parse.Boolean("m_bGlobalOrigin", globalOrigin);
            globalNormal = parse.Boolean("m_bGlobalNormal", globalNormal);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            maximumDistanceToControlPoint = parse.NumberProvider("m_flMaximumDistanceToCP", maximumDistanceToControlPoint);
            useOldCode = parse.Boolean("m_bUseOldCode", useOldCode);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var origin = globalOrigin
                ? pointOnPlane
                : ControlPointTransformProvider.TransformPosition(particleSystemState, controlPoint, pointOnPlane);

            var normal = globalNormal
                ? planeNormal
                : ControlPointTransformProvider.TransformDirection(particleSystemState, controlPoint, planeNormal);

            if (normal.LengthSquared() <= ParticleMath.MinimumLengthSquared)
            {
                return false;
            }

            normal = Vector3.Normalize(normal);

            var maxDistance = maximumDistanceToControlPoint.NextNumber(particleSystemState);

            var moved = false;
            var current = particles.Current;
            Span<float> depths = stackalloc float[4];

            for (var blockStart = 0; blockStart < current.Length; blockStart += 4)
            {
                var block = current.Slice(blockStart, Math.Min(4, current.Length - blockStart));
                var anyHeld = false;

                for (var lane = 0; lane < block.Length; lane++)
                {
                    ref var particle = ref block[lane];
                    var radius = particle.Radius * radiusScale.NextNumber(ref particle, particleSystemState);
                    depths[lane] = Vector3.Dot(particle.Position - origin, normal) - radius;

                    var withinReach = maxDistance <= 0f || Vector3.DistanceSquared(particle.Position, origin) < maxDistance * maxDistance;

                    if (!useOldCode && !withinReach)
                    {
                        depths[lane] = 0f;
                    }

                    anyHeld |= depths[lane] < 0f && withinReach;
                }

                if (!anyHeld)
                {
                    continue;
                }

                for (var lane = 0; lane < block.Length; lane++)
                {
                    if (depths[lane] < 0f)
                    {
                        block[lane].Position -= normal * depths[lane];
                        moved = true;
                    }
                }
            }

            return moved;
        }
    }
}
