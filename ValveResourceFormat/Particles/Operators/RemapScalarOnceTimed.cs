using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps one scalar attribute into another at a single moment, lerped toward by the operator
    /// strength. Proportional, each particle is remapped on the step its normalized age passes the
    /// remap time; otherwise every live particle is remapped once, on the step the system's age
    /// passes it, and particles born later are left alone until a restart.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapScalarOnceTimed">C_OP_RemapScalarOnceTimed</seealso>
    class RemapScalarOnceTimed : ParticleFunctionOperator
    {
        private readonly bool proportional;
        private readonly ParticleField fieldInput = ParticleField.Alpha;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly float remapTime = 1f;

        private bool done;

        public RemapScalarOnceTimed(ParticleDefinitionParser parse) : base(parse)
        {
            proportional = parse.Boolean("m_bProportional", proportional);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            remapTime = parse.Float("m_flRemapTime", remapTime);

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        public override void Reset() => done = false;

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (proportional)
            {
                OperateProportional(particles, frameTime, strength);
                return;
            }

            if (done || particleSystemState.Age <= remapTime)
            {
                return;
            }

            done = true;

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var inputs = current.ReadFloats(fieldInput);
            var outputs = current.Floats(fieldOutput);

            for (var i = 0; i < outputs.Length; i++)
            {
                outputs[i] = Remap(inputs[i], outputs[i], strength);
            }
        }

        private void OperateProportional(ParticleCollection particles, float frameTime, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var inputs = current.ReadFloats(fieldInput);
            var outputs = current.Floats(fieldOutput);

            for (var i = 0; i < outputs.Length; i++)
            {
                var lifetime = lifetimes[i] + ParticleMath.FloatEpsilon;
                var normalizedAge = ages[i] / lifetime;

                if (normalizedAge > remapTime && (frameTime / lifetime) + remapTime > normalizedAge)
                {
                    outputs[i] = Remap(inputs[i], outputs[i], strength);
                }
            }
        }

        private float Remap(float input, float output, float strength)
        {
            var value = MathUtils.RemapValClamped(input, inputMin, inputMax, outputMin, outputMax);
            return MathUtils.Lerp(output, value, strength);
        }
    }
}
