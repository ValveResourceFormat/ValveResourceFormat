using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RemapScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldInput = ParticleField.Alpha;
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax;
        private readonly float outputMin;
        private readonly float outputMax;

        public RemapScalar(ParticleDefinitionParser parse) : base(parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = particle.GetScalar(FieldInput);

            value = MathUtils.RemapRange(value, inputMin, inputMax, outputMin, outputMax);

            particle.SetScalar(FieldOutput, value);

            return particle;
        }
    }
}
