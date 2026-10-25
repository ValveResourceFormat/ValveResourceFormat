namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Remaps the edge length of a cube with the volume of the system's bounding box into the first
    /// component of a control point, clearing the other two. A degenerate input range is a step at
    /// <c>m_flInputMax</c>.
    /// </summary>
    /// <remarks>
    /// The box is the one last calculated for the system, so the value lags a frame behind, and a
    /// system without bounds gives <c>m_flOutputMin</c>.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapBoundingVolumetoCP">C_OP_RemapBoundingVolumetoCP</seealso>
    class RemapBoundingVolumetoCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int outControlPoint = -1;
        private readonly float inputMin;
        private readonly float inputMax = 128f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapBoundingVolumetoCP(ParticleDefinitionParser parse) : base(parse)
        {
            outControlPoint = parse.Int32("m_nOutControlPointNumber", outControlPoint);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (outControlPoint < 0)
            {
                return;
            }

            var value = outputMin;

            if (particleSystemState.Data?.WorldBoundingBox is { } bounds)
            {
                var size = bounds.Max - bounds.Min;
                var edge = MathF.Pow(size.X * size.Y * size.Z, 1f / 3f);

                if (!float.IsNaN(edge))
                {
                    value = MathUtils.RemapValClamped(edge, inputMin, inputMax, outputMin, outputMax);
                }
            }

            particleSystemState.SetControlPointValue(outControlPoint, new Vector3(value, 0f, 0f));
        }
    }
}
