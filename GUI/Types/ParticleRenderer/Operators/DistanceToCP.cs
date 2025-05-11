using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class DistanceToCP : ParticleFunctionOperator
    {
        private readonly float distanceMin;
        private readonly float distanceMax = 128;
        private readonly float outputMin;
        private readonly float outputMax = 1;
        private readonly int controlPoint;

        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool additive;
        private readonly bool activeRange;

        public DistanceToCP(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nFieldOutput", OutputField);
            distanceMin = parse.Float("m_flInputMin", distanceMin);
            distanceMax = parse.Float("m_flInputMax", distanceMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            controlPoint = parse.Int32("m_nStartCP", controlPoint);
            additive = parse.Boolean("m_bAdditive", additive);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);


            // Unsupported features: LOS test. We'd need collision for that.
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var cpPos = particleSystemState.GetControlPoint(controlPoint).Position;

            foreach (ref var particle in particles.Current)
            {
                var distance = Vector3.Distance(cpPos, particle.Position);

                // presumably triggered by activerange. untested but consistent with other modules behavior
                if (activeRange && (distance < distanceMin || distance > distanceMax))
                {
                    continue;
                }

                var remappedDistance = MathUtils.Remap(distance, distanceMin, distanceMax);

                remappedDistance = MathUtils.Saturate(remappedDistance);

                var finalValue = float.Lerp(outputMin, outputMax, remappedDistance);

                finalValue = particle.ModifyScalarBySetMethod(particles, OutputField, finalValue, setMethod);

                if (additive)
                {
                    // Yes, this causes it to continuously grow larger. Yes, this is in the original too.
                    finalValue += particle.GetScalar(OutputField);
                }
                particle.SetScalar(OutputField, finalValue);
            }
        }
    }
}
