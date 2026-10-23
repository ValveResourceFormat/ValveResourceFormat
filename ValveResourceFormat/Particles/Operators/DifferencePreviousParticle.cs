using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the distance between each particle's vector attribute and that of the particle before it
    /// in the collection to a scalar attribute. The first particle is never written.
    /// </summary>
    /// <remarks>
    /// A particle outside the active range is skipped, and the next one is compared with the last
    /// particle that was not. <c>m_bSetPreviousParticle</c> also multiplies the previous particle's
    /// output by the remapped value, whatever the set method. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DifferencePreviousParticle">C_OP_DifferencePreviousParticle</seealso>
    class DifferencePreviousParticle : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 128f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool setPreviousParticle;

        public DifferencePreviousParticle(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setPreviousParticle = parse.Boolean("m_bSetPreviousParticle", setPreviousParticle);

            if (fieldOutput.IsAlpha())
            {
                outputMin = ParticleMath.ClampToUnit(outputMin);
                outputMax = ParticleMath.ClampToUnit(outputMax);
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var previous = new Vector3(float.MaxValue);
            var previousIndex = 0;

            for (var i = 0; i < current.Length; i++)
            {
                var particle = current[i];
                var value = particle.GetVector(fieldInput);

                if (previous != new Vector3(float.MaxValue))
                {
                    var distance = Vector3.Distance(previous, value);

                    if (activeRange && (inputMin > distance || distance > inputMax))
                    {
                        continue;
                    }

                    var output = MathUtils.RemapValClamped(distance, inputMin, inputMax, outputMin, outputMax);

                    particle.SetScalar(fieldOutput, particle.ModifyScalarBySetMethod(particles, fieldOutput, output, setMethod));

                    if (setPreviousParticle)
                    {
                        var previousParticle = current[previousIndex];
                        previousParticle.SetScalar(fieldOutput, output * previousParticle.GetScalar(fieldOutput));
                    }
                }

                previous = value;
                previousIndex = i;
            }
        }
    }
}
