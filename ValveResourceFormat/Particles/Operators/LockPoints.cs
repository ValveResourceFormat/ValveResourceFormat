namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Pulls the particles from index <c>m_nMinCol</c> to <c>m_nMaxCol</c> toward a control point,
    /// keeping a blend value's share of their own position each step. With the defaults the first
    /// particle is snapped onto the control point.
    /// </summary>
    /// <remarks>
    /// The row fields are not read. The previous position is not moved, so the pull shows up as
    /// velocity on the next step.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LockPoints">C_OP_LockPoints</seealso>
    class LockPoints : ParticleFunctionOperator
    {
        private readonly int minCol;
        private readonly int maxCol;
        private readonly int controlPoint;
        private readonly float blendValue;

        public LockPoints(ParticleDefinitionParser parse) : base(parse)
        {
            minCol = parse.Int32("m_nMinCol", minCol);
            maxCol = parse.Int32("m_nMaxCol", maxCol);
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
            blendValue = parse.Float("m_flBlendValue", blendValue);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var blend = MathUtils.Lerp(1f, blendValue, strength);
            var target = particleSystemState.GetControlPoint(controlPoint).Position;
            var last = Math.Min(maxCol, current.Length - 1);

            for (var i = Math.Max(minCol, 0); i <= last; i++)
            {
                positions[i] = MathUtils.Lerp(target, positions[i], blend);
            }
        }
    }
}
