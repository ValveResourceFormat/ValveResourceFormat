using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateWithinBox : IParticleInitializer
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);

        private readonly int controlPointNumber;
        private readonly int scaleCP = -1;

        public CreateWithinBox(ParticleDefinitionParser parse)
        {
            min = parse.VectorProvider("m_vecMin", min);

            max = parse.VectorProvider("m_vecMax", max);

            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);

            scaleCP = parse.Int32("m_nScaleCP", scaleCP);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var posMin = min.NextVector(ref particle, particleSystemState);
            var posMax = max.NextVector(ref particle, particleSystemState);

            var position = MathUtils.RandomBetweenPerComponent(posMin, posMax);

            var offset = particleSystemState.GetControlPoint(controlPointNumber).Position;

            if (scaleCP > -1)
            {
                // Scale CP uses X position as scale value. Not applied to the CP Offset
                position *= particleSystemState.GetControlPoint(scaleCP).Position.X;
            }

            particle.InitialPosition += position + offset;
            particle.Position = particle.InitialPosition;

            return particle;
        }
    }
}
