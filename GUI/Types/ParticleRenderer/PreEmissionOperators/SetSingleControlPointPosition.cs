namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetSingleControlPointPosition : ParticleFunctionPreEmissionOperator
    {
        private readonly int CP1 = 1;
        private readonly IVectorProvider CP1Pos = new LiteralVectorProvider(new Vector3(128, 0, 0));

        private readonly bool SetOnce;
        private readonly bool UseWorldLocation;
        private readonly int CPOffset;
        // The m_bUseWorldLocation parameter would set the CP positions in world space instead of object space. How do we do that?

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
                // not fully accurate, as it's still in local space, but it's closer to correct
                var controlPointOffset = UseWorldLocation
                    ? Vector3.Zero
                    : particleSystemState.GetControlPoint(CPOffset).Position;

                particleSystemState.SetControlPointValue(CP1, CP1Pos.NextVector(particleSystemState) + controlPointOffset);

                HasRunBefore = true;
            }
        }
    }
}
