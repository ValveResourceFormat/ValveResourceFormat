namespace ValveResourceFormat.Particles.Operators
{
    class DistanceBetweenVecs : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly IVectorProvider point1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider point2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool deltaTime;

        public DistanceBetweenVecs(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            point1 = parse.VectorProvider("m_vecPoint1", point1);
            point2 = parse.VectorProvider("m_vecPoint2", point2);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            deltaTime = parse.Boolean("m_bDeltaTime", deltaTime);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var divisor = deltaTime ? frameTime : 1f;

            foreach (ref var particle in particles.Current)
            {
                var vec1 = point1.NextVector(ref particle, particleSystemState);
                var vec2 = point2.NextVector(ref particle, particleSystemState);

                var min = inputMin.NextNumber(ref particle, particleSystemState);
                var max = inputMax.NextNumber(ref particle, particleSystemState);
                var span = min == max ? 1f : max - min;

                var distance = Vector3.Distance(vec1, vec2) / divisor;
                var fraction = MathUtils.Saturate((distance - min) / span);

                var value = float.Lerp(
                    outputMin.NextNumber(ref particle, particleSystemState),
                    outputMax.NextNumber(ref particle, particleSystemState),
                    fraction);

                var target = particle.ModifyScalarBySetMethod(particles, fieldOutput, value, setMethod);

                particle.SetScalar(fieldOutput, float.Lerp(particle.GetScalar(fieldOutput), target, strength));
            }
        }
    }
}
