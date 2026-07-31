namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a single control point to a specified position, optionally offset from another control
    /// point and optionally only once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetSingleControlPointPosition">C_OP_SetSingleControlPointPosition</seealso>
    class SetSingleControlPointPosition : ParticleFunctionPreEmissionOperator
    {
        private readonly int CP1 = 1;
        private readonly IVectorProvider CP1Pos = new LiteralVectorProvider(new Vector3(128, 0, 0));

        private readonly bool SetOnce;
        private readonly bool UseWorldLocation;
        private readonly int CPOffset;

        private bool HasRunBefore;

        public SetSingleControlPointPosition(ParticleDefinitionParser parse) : base(parse)
        {
            CP1 = parse.Int32("m_nCP1", CP1);
            CP1Pos = parse.VectorProvider("m_vecCP1Pos", CP1Pos);
            SetOnce = parse.Boolean("m_bSetOnce", SetOnce);
            UseWorldLocation = parse.Boolean("m_bUseWorldLocation", UseWorldLocation);
            CPOffset = parse.Int32("m_nHeadLocation", CPOffset);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(SetOnce && HasRunBefore))
            {
                var position = CP1Pos.NextVector(particleSystemState);

                if (!UseWorldLocation)
                {
                    // The position is an offset in the head control point's frame.
                    position = ControlPointTransformProvider.TransformPosition(particleSystemState, CPOffset, position);
                }

                particleSystemState.SetControlPointValue(CP1, position);

                HasRunBefore = true;
            }
        }
    }
}
