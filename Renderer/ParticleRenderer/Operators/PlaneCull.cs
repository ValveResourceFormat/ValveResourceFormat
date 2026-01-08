namespace ValveResourceFormat.Renderer.Particles.Operators
{
    class PlaneCull : ParticleFunctionOperator
    {
        private readonly int cp;
        private readonly float planeOffset;
        private readonly Vector3 planeNormal = new(0, 0, 1);
        private readonly bool localSpace;

        public PlaneCull(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nPlaneControlPoint", cp);

            planeNormal = Vector3.Normalize(parse.Vector3("m_vecPlaneDirection", planeNormal));

            planeOffset = parse.Float("m_flPlaneOffset", planeOffset);

            // currently does nothing
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        }
        private bool CulledByPlane(Vector3 position, ParticleSystemRenderState particleSystemState)
        {
            var pointOnPlane = particleSystemState.GetControlPoint(cp).Position;

            // Offset in normal direction by planeOffset
            pointOnPlane -= (planeNormal * planeOffset);

            var sign = Vector3.Dot(planeNormal, position - pointOnPlane);
            return sign < 0;
        }
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (CulledByPlane(particle.Position, particleSystemState))
                {
                    particle.Kill();
                }
            }
        }
    }
}
