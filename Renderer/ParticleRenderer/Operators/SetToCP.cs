namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Snaps every particle onto a control point plus an offset, in world space or in the control
    /// point's own frame. The particle's motion is cleared along with its position.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetToCP">C_OP_SetToCP</seealso>
    class SetToCP : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly Vector3 offset = Vector3.Zero;
        private readonly bool offsetLocal;

        public SetToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            offset = parse.Vector3("m_vecOffset", offset);
            offsetLocal = parse.Boolean("m_bOffsetLocal", offsetLocal);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var cp = particleSystemState.GetControlPoint(controlPoint);

            var position = offsetLocal
                ? cp.Position + Vector3.Transform(offset, cp.GetRotation())
                : cp.Position + offset;

            foreach (ref var particle in particles.Current)
            {
                particle.Position = position;
                particle.PositionPrevious = position;
            }
        }
    }
}
