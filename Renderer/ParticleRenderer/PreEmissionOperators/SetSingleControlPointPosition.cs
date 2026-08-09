namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a single control point to a specified position, optionally offset from another control
    /// point and optionally only once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetSingleControlPointPosition">C_OP_SetSingleControlPointPosition</seealso>
    class SetSingleControlPointPosition : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly IVectorProvider cp1Pos = new LiteralVectorProvider(new Vector3(128, 0, 0));

        private readonly bool setOnce;
        private readonly bool useWorldLocation;
        private readonly int cpOffset;

        private bool hasRunBefore;

        public SetSingleControlPointPosition(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.Int32("m_nCP1", cp1);
            cp1Pos = parse.VectorProvider("m_vecCP1Pos", cp1Pos);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            cpOffset = parse.Int32("m_nHeadLocation", cpOffset);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(setOnce && hasRunBefore))
            {
                var position = cp1Pos.NextVector(particleSystemState);

                if (!useWorldLocation)
                {
                    // The position is an offset in the head control point's frame.
                    position = ControlPointTransformProvider.TransformPosition(particleSystemState, cpOffset, position);
                }

                particleSystemState.SetControlPointValue(cp1, position);

                hasRunBefore = true;
            }
        }
    }
}
