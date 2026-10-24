using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the distance between two per-particle vector inputs, or that distance per second with
    /// <c>m_bDeltaTime</c>, to a scalar attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceBetweenVecs">C_OP_DistanceBetweenVecs</seealso>
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
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            deltaTime = parse.Boolean("m_bDeltaTime", deltaTime);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var divisor = deltaTime ? frameTime : 1f;

            foreach (var particle in particles.Current)
            {
                var distance = Vector3.Distance(point1.NextVector(particle, particleSystemState), point2.NextVector(particle, particleSystemState)) / divisor;

                var inputMin = this.inputMin.NextNumber(particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(particle, particleSystemState);
                var outputMin = this.outputMin.NextNumber(particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(particle, particleSystemState);

                var fraction = ParticleMath.ClampedFraction(distance, inputMin, inputMax);
                var value = particle.ModifyScalarBySetMethod(particles, fieldOutput, MathUtils.Lerp(outputMin, outputMax, fraction), setMethod);

                particle.SetScalar(fieldOutput, MathUtils.Lerp(particle.GetScalar(fieldOutput), value, strength));
            }
        }
    }
}
