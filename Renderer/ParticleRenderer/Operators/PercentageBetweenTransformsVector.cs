namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Writes how far along each particle is between two transform origins into a vector output
    /// field, lerping every component of the output range by the same scalar percentage. The
    /// percentage is computed exactly as in <see cref="PercentageBetweenTransforms"/>.
    /// </summary>
    /// <remarks>
    /// The engine implementation never reads operator strength, so the strength passed to
    /// <see cref="Operate"/> is ignored.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PercentageBetweenTransformsVector">C_OP_PercentageBetweenTransformsVector</seealso>
    class PercentageBetweenTransformsVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool radialCheck = true;

        public PercentageBetweenTransformsVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            radialCheck = parse.Boolean("m_bRadialCheck", radialCheck);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var length = Vector3.Distance(start, end);

            foreach (ref var particle in particles.Current)
            {
                var percentage = PercentageBetweenTransforms.Percentage(particle.Position, start, end, length, radialCheck);

                if (activeRange && !(inputMin <= percentage && percentage <= inputMax))
                {
                    continue;
                }

                var divisor = inputMax == inputMin ? 1f : inputMax - inputMin;
                var finalValue = Vector3.Lerp(outputMin, outputMax, MathUtils.Saturate((percentage - inputMin) / divisor));

                finalValue = particle.ModifyVectorBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetVector(outputField, finalValue);
            }
        }
    }
}
