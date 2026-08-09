namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets the positions of up to four control points to fixed locations, optionally offset
    /// from a parent control point and optionally only once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointPositions">C_OP_SetControlPointPositions</seealso>
    class SetControlPointPositions : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly int cp2 = 2;
        private readonly int cp3 = 3;
        private readonly int cp4 = 4;
        private readonly Vector3 cp1Pos = new(128, 0, 0);
        private readonly Vector3 cp2Pos = new(0, 128, 0);
        private readonly Vector3 cp3Pos = new(-128, 0, 0);
        private readonly Vector3 cp4Pos = new(0, -128, 0);

        private readonly bool setOnce;
        private readonly bool useWorldLocation;
        private readonly int cpOffset;

        private bool hasRunBefore;

        public SetControlPointPositions(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.Int32("m_nCP1", cp1);
            cp2 = parse.Int32("m_nCP2", cp2);
            cp3 = parse.Int32("m_nCP3", cp3);
            cp4 = parse.Int32("m_nCP4", cp4);
            cp1Pos = parse.Vector3("m_vecCP1Pos", cp1Pos);
            cp2Pos = parse.Vector3("m_vecCP2Pos", cp2Pos);
            cp3Pos = parse.Vector3("m_vecCP3Pos", cp3Pos);
            cp4Pos = parse.Vector3("m_vecCP4Pos", cp4Pos);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            cpOffset = parse.Int32("m_nHeadLocation", cpOffset);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(setOnce && hasRunBefore))
            {
                // Object-space positions are rotated and translated by the head control point
                var headTransform = useWorldLocation
                    ? Matrix4x4.Identity
                    : new ControlPointTransformProvider(cpOffset, true).NextTransform(ref Particle.Default, particleSystemState);

                particleSystemState.SetControlPointValue(cp1, Vector3.Transform(cp1Pos, headTransform));
                particleSystemState.SetControlPointValue(cp2, Vector3.Transform(cp2Pos, headTransform));
                particleSystemState.SetControlPointValue(cp3, Vector3.Transform(cp3Pos, headTransform));
                particleSystemState.SetControlPointValue(cp4, Vector3.Transform(cp4Pos, headTransform));

                hasRunBefore = true;
            }
        }
    }
}
