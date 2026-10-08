using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps particles on the front side of a plane, pushing any that sink behind it back out until
    /// their radius rests on it. The plane is placed in a control point's frame unless authored global.
    /// With a maximum distance, only particles closer than it to the plane's origin are held.
    /// </summary>
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

        public PlanarConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            pointOnPlane = parse.Vector3("m_PointOnPlane", pointOnPlane);
            planeNormal = parse.Vector3("m_PlaneNormal", planeNormal);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            globalOrigin = parse.Boolean("m_bGlobalOrigin", globalOrigin);
            globalNormal = parse.Boolean("m_bGlobalNormal", globalNormal);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            maximumDistanceToControlPoint = parse.NumberProvider("m_flMaximumDistanceToCP", maximumDistanceToControlPoint);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
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

            foreach (ref var particle in particles.Current)
            {
                if (maxDistance > 0f && Vector3.DistanceSquared(particle.Position, origin) >= maxDistance * maxDistance)
                {
                    continue;
                }

                var radius = particle.Radius * radiusScale.NextNumber(ref particle, particleSystemState);
                var depth = Vector3.Dot(particle.Position - origin, normal) - radius;

                if (depth < 0f)
                {
                    particle.Position -= normal * depth;
                    moved = true;
                }
            }

            return moved;
        }
    }
}
