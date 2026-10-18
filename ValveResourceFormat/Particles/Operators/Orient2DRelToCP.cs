namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Every step, turns an angle attribute toward the direction from a control point to the particle
    /// in the world XY plane, plus an offset in degrees. The angle is the XY heading plus pi, as
    /// <see cref="Initializers.Orient2DRelToCP"/> computes it.
    /// </summary>
    /// <remarks>
    /// Each step moves the attribute <c>m_flSpinStrength</c> of the way to the target, independent of
    /// the step length and of the operator strength, and without wrapping, so a spin strength below 1
    /// can swing the long way round when the target crosses 0.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Orient2DRelToCP">C_OP_Orient2DRelToCP</seealso>
    class Orient2DRelToCP : ParticleFunctionOperator
    {
        private readonly float rotOffset;
        private readonly float spinStrength = 1f;
        private readonly int cp;
        private readonly ParticleField fieldOutput = ParticleField.Roll;

        public Orient2DRelToCP(ParticleDefinitionParser parse) : base(parse)
        {
            rotOffset = float.DegreesToRadians(parse.Float("m_flRotOffset", 0f));
            spinStrength = parse.Float("m_flSpinStrength", spinStrength);
            cp = parse.Int32("m_nCP", cp);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var center = particleSystemState.GetControlPoint(cp).Position;

            foreach (ref var particle in particles.Current)
            {
                if (Initializers.Orient2DRelToCP.Angle(particle.Position, center) is not { } angle)
                {
                    continue;
                }

                particle.SetScalar(fieldOutput, MathUtils.Lerp(particle.GetScalar(fieldOutput), angle + rotOffset, spinStrength));
            }
        }
    }
}
