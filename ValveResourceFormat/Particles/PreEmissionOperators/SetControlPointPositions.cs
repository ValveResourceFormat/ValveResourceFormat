namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets the positions of up to four control points to fixed locations, optionally offset
    /// from a parent control point and optionally only once. With <c>m_bOrient</c> the offset control
    /// points also take the head control point's orientation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointPositions">C_OP_SetControlPointPositions</seealso>
    class SetControlPointPositions : ParticleFunctionPreEmissionOperator
    {
        private readonly (int ControlPoint, Vector3 Position)[] points;

        private readonly bool setOnce;
        private readonly bool useWorldLocation;
        private readonly int cpOffset;
        private readonly bool orient;

        private bool hasRunBefore;

        public SetControlPointPositions(ParticleDefinitionParser parse) : base(parse)
        {
            points =
            [
                (parse.Int32("m_nCP1", 1), parse.Vector3("m_vecCP1Pos", new Vector3(128f, 0f, 0f))),
                (parse.Int32("m_nCP2", 2), parse.Vector3("m_vecCP2Pos", new Vector3(0f, 128f, 0f))),
                (parse.Int32("m_nCP3", 3), parse.Vector3("m_vecCP3Pos", new Vector3(-128f, 0f, 0f))),
                (parse.Int32("m_nCP4", 4), parse.Vector3("m_vecCP4Pos", new Vector3(0f, -128f, 0f))),
            ];
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            cpOffset = parse.Int32("m_nHeadLocation", cpOffset);
            orient = parse.Boolean("m_bOrient", orient);
        }

        public override void Reset()
        {
            hasRunBefore = false;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (setOnce)
            {
                if (hasRunBefore)
                {
                    return;
                }

                hasRunBefore = true;
            }

            if (useWorldLocation)
            {
                foreach (var (controlPoint, position) in points)
                {
                    particleSystemState.SetControlPointValue(controlPoint, position);
                }

                return;
            }

            var head = particleSystemState.GetControlPoint(cpOffset);
            var headTransform = head.GetTransform();

            foreach (var (controlPoint, position) in points)
            {
                particleSystemState.SetControlPointValue(controlPoint, Vector3.Transform(position, headTransform));
            }

            if (orient)
            {
                foreach (var (controlPoint, _) in points)
                {
                    particleSystemState.SetControlPointOrientation(controlPoint, head.Forward, head.Left, head.Up);
                }
            }
        }
    }
}
