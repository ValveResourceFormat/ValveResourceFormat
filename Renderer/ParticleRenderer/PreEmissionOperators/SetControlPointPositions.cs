namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    class SetControlPointPositions : ParticleFunctionPreEmissionOperator
    {
        private readonly int CP1 = 1;
        private readonly int CP2 = 2;
        private readonly int CP3 = 3;
        private readonly int CP4 = 4;
        private readonly Vector3 CP1Pos = new(128, 0, 0);
        private readonly Vector3 CP2Pos = new(0, -128, 0);
        private readonly Vector3 CP3Pos = new(-128, 0, 0);
        private readonly Vector3 CP4Pos = new(0, -128, 0);

        private readonly bool setOnce;
        private readonly bool useWorldLocation;
        private readonly int CPOffset;
        // The m_bUseWorldLocation parameter would set the CP positions in world space instead of object space. How do we do that?

        private bool HasRunBefore;

        public SetControlPointPositions(ParticleDefinitionParser parse) : base(parse)
        {
            CP1 = parse.Int32("m_nCP1", CP1);
            CP2 = parse.Int32("m_nCP2", CP2);
            CP3 = parse.Int32("m_nCP3", CP3);
            CP4 = parse.Int32("m_nCP4", CP4);
            CP1Pos = parse.Vector3("m_vecCP1Pos", CP1Pos);
            CP2Pos = parse.Vector3("m_vecCP2Pos", CP2Pos);
            CP3Pos = parse.Vector3("m_vecCP3Pos", CP3Pos);
            CP4Pos = parse.Vector3("m_vecCP4Pos", CP4Pos);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            CPOffset = parse.Int32("m_nHeadLocation", CPOffset);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!(setOnce && HasRunBefore))
            {
                // not fully accurate, as it is still in local space, but it's closer to correct
                var controlPointOffset = useWorldLocation
                    ? Vector3.Zero
                    : particleSystemState.GetControlPoint(CPOffset).Position;

                particleSystemState.SetControlPointValue(CP1, CP1Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP2, CP2Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP3, CP3Pos + controlPointOffset);
                particleSystemState.SetControlPointValue(CP4, CP4Pos + controlPointOffset);

                HasRunBefore = true;
            }
        }
    }
}
