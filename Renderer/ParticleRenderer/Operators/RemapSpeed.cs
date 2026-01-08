using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RemapSpeed : ParticleFunctionOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public RemapSpeed(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nFieldOutput", OutputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);
                var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                var finalValue = MathUtils.RemapRange(particle.Speed, inputMin, inputMax, outputMin, outputMax);
                finalValue = particle.ModifyScalarBySetMethod(particles, OutputField, finalValue, setMethod);

                particle.SetScalar(OutputField, finalValue);
            }
        }
    }
}
