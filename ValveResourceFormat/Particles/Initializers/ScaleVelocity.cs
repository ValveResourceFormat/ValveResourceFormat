using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Scales the velocity earlier initializers gave a particle, per axis.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_ScaleVelocity">C_INIT_ScaleVelocity</seealso>
    class ScaleVelocity : ParticleFunctionInitializer
    {
        private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);

        public ScaleVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.VectorProvider("m_vecScale", scale);
        }

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The scale is evaluated for every live particle, and each particle of the run reads its own value
            var scaleValues = scale.Prepare(run, particles.Current, particleSystemState);
            run.FillPerBlockInputs(particleSystemState, scale, scaleValues, blocksFromStart: true);

            var scales = MemoryMarshal.Cast<Vector3, float>(scaleValues);

            var positions = MemoryMarshal.Cast<Vector3, float>(run.ReadVectors(ParticleField.Position));
            var previousPositions = MemoryMarshal.Cast<Vector3, float>(run.Vectors(ParticleField.PositionPrevious));

            TensorPrimitives.Subtract(previousPositions, positions, previousPositions);
            TensorPrimitives.MultiplyAdd(previousPositions, scales, positions, previousPositions);
        }
    }
}
