namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Writes how far along each particle is between two transform origins into a scalar output
    /// field: with the radial check on, the ratio of the particle's distance from the start to
    /// the distance between the transforms; otherwise its unclamped projection onto the
    /// start-to-end segment. The output range is clamped to [0, 1] at load time when the output
    /// field is an alpha.
    /// </summary>
    /// <remarks>
    /// The engine implementation never reads operator strength, so the strength passed to
    /// <see cref="Operate"/> is ignored.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PercentageBetweenTransforms">C_OP_PercentageBetweenTransforms</seealso>
    class PercentageBetweenTransforms : ParticleFunctionOperator
    {
        internal const float FltEpsilon = 1.1920929e-7f;

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool radialCheck = true;

        public PercentageBetweenTransforms(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            radialCheck = parse.Boolean("m_bRadialCheck", radialCheck);

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var length = Vector3.Distance(start, end);

            foreach (ref var particle in particles.Current)
            {
                var percentage = Percentage(particle.Position, start, end, length, radialCheck);

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

        /// <summary>
        /// How far along the start-to-end span a position lies: with <paramref name="radialCheck"/>
        /// the ratio of its distance from the start to <paramref name="length"/> (never negative,
        /// unbounded above), otherwise its unclamped projection onto the segment (0 at the start,
        /// 1 at the end).
        /// </summary>
        internal static float Percentage(Vector3 position, Vector3 start, Vector3 end, float length, bool radialCheck)
        {
            if (radialCheck)
            {
                return 1f / ((length + FltEpsilon) / (Vector3.Distance(position, start) + FltEpsilon));
            }

            var axis = end - start;
            var lengthSquared = Vector3.Dot(axis, axis);
            return lengthSquared < 1e-5f ? 0f : Vector3.Dot(axis, position - start) / lengthSquared;
        }
    }
}
