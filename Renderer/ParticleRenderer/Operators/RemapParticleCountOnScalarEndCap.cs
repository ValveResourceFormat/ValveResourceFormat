namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's slot in the collection to a scalar output range, once, on the frame the
    /// endcap starts. Only the slots inside the input range are written.
    ///
    /// <para>Counting backwards takes the slots in from the end of the collection, hands them the output
    /// range the other way round, and shortens the high end of the input range by one slot.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapParticleCountOnScalarEndCap">C_OP_RemapParticleCountOnScalarEndCap</seealso>
    class RemapParticleCountOnScalarEndCap : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly int inputMin;
        private readonly int inputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool backwards;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        private bool hasRun;

        public RemapParticleCountOnScalarEndCap(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Int32("m_nInputMin", inputMin);
            inputMax = parse.Int32("m_nInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            backwards = parse.Boolean("m_bBackwards", backwards);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }
        }

        public override void Reset()
        {
            hasRun = false;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (hasRun || !particleSystemState.InEndCap)
            {
                return;
            }

            hasRun = true;

            var rangeStart = Math.Clamp(backwards ? particles.Count - inputMax - 1 : inputMin, 0, particles.Count);
            var rangeEnd = Math.Clamp(backwards ? particles.Count - inputMin : inputMax, 0, particles.Count);

            var first = backwards ? outputMax : outputMin;
            var last = backwards ? outputMin : outputMax;
            var inputEnd = (float)(backwards ? rangeEnd - 1 : rangeEnd);

            for (var i = rangeStart; i < rangeEnd; i++)
            {
                ref var particle = ref particles.Current[i];

                var finalValue = MathUtils.RemapValClamped(i, rangeStart, inputEnd, first, last);

                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), finalValue, strength));
            }
        }
    }
}
