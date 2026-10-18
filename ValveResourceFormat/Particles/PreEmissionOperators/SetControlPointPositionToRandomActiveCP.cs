using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Moves a control point to the position of a randomly chosen control point among
    /// <c>m_nHeadLocationMin</c> up to but not including <c>m_nHeadLocationMax</c> that has been set,
    /// once, or every <c>m_flResetRate</c> seconds when that is not 0.
    /// </summary>
    /// <remarks>Nothing is chosen while the system's clock is still at 0.</remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointPositionToRandomActiveCP">C_OP_SetControlPointPositionToRandomActiveCP</seealso>
    class SetControlPointPositionToRandomActiveCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly int headLocationMin;
        private readonly int headLocationMax;
        private readonly INumberProvider resetRate = new LiteralNumberProvider(0f);

        private readonly List<int> activeControlPoints = [];
        private float nextResetTime;

        public SetControlPointPositionToRandomActiveCP(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.Int32("m_nCP1", cp1);
            headLocationMin = parse.Int32("m_nHeadLocationMin", headLocationMin);
            headLocationMax = parse.Int32("m_nHeadLocationMax", headLocationMax);
            resetRate = parse.NumberProvider("m_flResetRate", resetRate);
        }

        public override void Reset()
        {
            nextResetTime = ParticleMath.FloatMin;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (nextResetTime > particleSystemState.Age)
            {
                return;
            }

            var rate = resetRate.NextNumber(particleSystemState);
            nextResetTime = rate == 0f ? float.MaxValue : rate + particleSystemState.Age;

            activeControlPoints.Clear();

            for (var cp = Math.Max(0, headLocationMin); cp < headLocationMax; cp++)
            {
                if (cp <= particleSystemState.HighestControlPoint && particleSystemState.GetControlPoint(cp).ChangeTime != -1f)
                {
                    activeControlPoints.Add(cp);
                }
            }

            if (activeControlPoints.Count == 0)
            {
                return;
            }

            var pick = activeControlPoints[particleSystemState.Random.NextIndex(activeControlPoints.Count)];
            particleSystemState.SetControlPointValue(cp1, particleSystemState.GetControlPoint(pick).Position);
        }
    }
}
