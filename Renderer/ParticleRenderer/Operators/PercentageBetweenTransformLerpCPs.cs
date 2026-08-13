namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Writes how far along each particle is between two transform origins into a scalar output
    /// field - despite the name it does not write control points. The output range endpoints are
    /// read once per frame from a position component (0-2, clamped at load time) of two control
    /// points. The percentage is computed exactly as in <see cref="PercentageBetweenTransforms"/>.
    /// </summary>
    /// <remarks>
    /// The engine implementation never reads operator strength, so the strength passed to
    /// <see cref="Operate"/> is ignored.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PercentageBetweenTransformLerpCPs">C_OP_PercentageBetweenTransformLerpCPs</seealso>
    class PercentageBetweenTransformLerpCPs : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly int outputStartCP = 2;
        private readonly int outputStartField;
        private readonly int outputEndCP = 2;
        private readonly int outputEndField;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool radialCheck = true;

        public PercentageBetweenTransformLerpCPs(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            outputStartCP = parse.Int32("m_nOutputStartCP", outputStartCP);
            outputStartField = Math.Clamp(parse.Int32("m_nOutputStartField", outputStartField), 0, 2);
            outputEndCP = parse.Int32("m_nOutputEndCP", outputEndCP);
            outputEndField = Math.Clamp(parse.Int32("m_nOutputEndField", outputEndField), 0, 2);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            radialCheck = parse.Boolean("m_bRadialCheck", radialCheck);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var length = Vector3.Distance(start, end);

            var outputMin = particleSystemState.GetControlPoint(outputStartCP).Position.GetComponent(outputStartField);
            var outputMax = particleSystemState.GetControlPoint(outputEndCP).Position.GetComponent(outputEndField);

            foreach (ref var particle in particles.Current)
            {
                var percentage = PercentageBetweenTransforms.Percentage(particle.Position, start, end, length, radialCheck);

                if (activeRange && !(inputMin <= percentage && percentage <= inputMax))
                {
                    continue;
                }

                var divisor = inputMax == inputMin ? 1f : inputMax - inputMin;
                var finalValue = float.Lerp(outputMin, outputMax, MathUtils.Saturate((percentage - inputMin) / divisor));

                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, finalValue);
            }
        }
    }
}
