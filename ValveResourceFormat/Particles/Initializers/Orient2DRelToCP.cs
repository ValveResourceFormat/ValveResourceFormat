using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ValveResourceFormat.Particles.Initializers
{
    class Orient2DRelToCP : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly float rotationOffset;

        public Orient2DRelToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nCP", controlPoint);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            rotationOffset = float.DegreesToRadians(parse.Float("m_flRotOffset", 0f));
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var position = particleSystemState.GetControlPoint(controlPoint).Position;
            var x = particle.Position.X - position.X;
            var y = particle.Position.Y - position.Y;

            if (x == 0f && y == 0f)
            {
                return particle;
            }

            var lengthSquared = ((x * x + 1.1920929e-7f) + y * y) + 0f;
            var inverseLength = Sse.IsSupported
                ? Sse.ReciprocalSqrtScalar(Vector128.CreateScalar(lengthSquared)).ToScalar()
                : MathF.ReciprocalSqrtEstimate(lengthSquared);
            inverseLength = (3f - lengthSquared * inverseLength * inverseLength) * (inverseLength * 0.5f);
            var angle = MathF.Atan2(y * inverseLength, x * inverseLength) + MathF.PI + rotationOffset;
            particle.SetScalar(fieldOutput, angle);

            return particle;
        }
    }
}
