namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills particles that cross to the negative side of a plane. The plane is defined by a
    /// control point as the origin and a configurable normal direction.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PlaneCull">C_OP_PlaneCull</seealso>
    class PlaneCull : ParticleFunctionOperator
    {
        private readonly int cp;
        private readonly float planeOffset;
        private readonly IVectorProvider planeDirection = new LiteralVectorProvider(new Vector3(0, 0, 1));
        private readonly bool localSpace;

        public PlaneCull(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nPlaneControlPoint", cp);

            planeDirection = parse.VectorProvider("m_vecPlaneDirection", planeDirection);

            planeOffset = parse.Float("m_flPlaneOffset", planeOffset);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var direction = planeDirection.NextVector(particleSystemState);
            if (direction == Vector3.Zero)
            {
                return;
            }

            // In local space the plane direction is authored in the control point's frame.
            if (localSpace)
            {
                direction = ControlPointTransformProvider.TransformDirection(particleSystemState, cp, direction);
            }

            var planeNormal = MathUtils.SafeNormalize(direction);

            var pointOnPlane = particleSystemState.GetControlPoint(cp).Position;
            pointOnPlane -= planeNormal * planeOffset;

            var positions = particles.Current.ReadVectors(ParticleField.Position);

            for (var i = 0; i < positions.Length; i++)
            {
                var sign = Vector3.Dot(planeNormal, positions[i] - pointOnPlane);

                if (sign < 0)
                {
                    KillCulled(particles, i, particleSystemState);
                }
            }
        }
    }
}
