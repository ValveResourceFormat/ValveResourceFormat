using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Copies one attribute from a parent particle to each new child particle, scaled for scalar and
    /// vector fields and copied as is for integer fields and bone indices, walking the parent list with
    /// a running index that resets to zero past the end rather than wrapping modulo, and keeps its
    /// place across a restart. Color and alpha class fields clamp to [0, 1] after scaling.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InheritFromParentParticles">C_INIT_InheritFromParentParticles</seealso>
    class InheritFromParentParticles : ParticleFunctionInitializer
    {
        private readonly float scale = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int increment = 1;
        private readonly bool randomDistribution;
        private readonly int randomSeed;

        /// <summary>The parent particle attributes are only written from behavior version 9.</summary>
        private readonly bool writeParentIndex;

        private readonly ParticleFieldStorage storage;

        private int runningIndex;
        private int randomCounter;

        public InheritFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.Float("m_flScale", scale);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            storage = ParticleBuffer.StorageOf(fieldOutput);
            increment = parse.Int32("m_nIncrement", increment);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            writeParentIndex = parse.BehaviorVersion >= 9;
        }

        public override void Reset()
        {
            runningIndex = 0;
            randomCounter = 0;
        }

        public override ulong WrittenFields
            => fieldOutput.Mask() | ParticleField.ParentParticleIndex.Mask() | ParticleField.ParentParticleId.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var parentData = particleSystemState.ParentSystem?.Data;
            if (parentData == null)
            {
                return;
            }

            var parentParticles = parentData.CurrentParticles;
            var count = parentParticles.Length;
            if (count == 0)
            {
                return;
            }

            var boneIndices = storage == ParticleFieldStorage.BoneIndices ? run.BoneIndices : default;
            var vectorOutput = storage == ParticleFieldStorage.Vector ? run.Vectors(fieldOutput) : default;
            var scalarOutput = (storage is ParticleFieldStorage.Integer or ParticleFieldStorage.Scalar) ? run.Floats(fieldOutput) : default;
            var parentIndices = writeParentIndex ? run.Integers(ParticleField.ParentParticleIndex) : default;
            var parentIds = writeParentIndex ? run.Integers(ParticleField.ParentParticleId) : default;

            var parentVectors = storage == ParticleFieldStorage.Vector ? parentParticles.ReadVectors(fieldOutput) : default;
            var parentScalars = (storage is ParticleFieldStorage.Integer or ParticleFieldStorage.Scalar) ? parentParticles.ReadFloats(fieldOutput) : default;
            var parentParticleIds = writeParentIndex ? parentParticles.ReadIntegers(ParticleField.ParticleId) : default;

            for (var i = 0; i < run.Length; i++)
            {
                int index;
                if (randomDistribution)
                {
                    var fraction = particleSystemState.Random.NextOrSeeded(randomSeed, ref randomCounter);
                    index = (int)(count * fraction);
                    runningIndex = index;
                }
                else if (runningIndex < 0 || runningIndex > count - 1)
                {
                    runningIndex = 0;
                    index = 0;
                }
                else
                {
                    index = runningIndex;
                }

                switch (storage)
                {
                    case ParticleFieldStorage.BoneIndices:
                        boneIndices[i] = parentParticles.ReadBoneIndices(index);
                        break;
                    case ParticleFieldStorage.Vector:
                        var vector = parentVectors[index] * scale;

                        if (fieldOutput.IsNormalizedField())
                        {
                            vector = Vector3.Clamp(vector, Vector3.Zero, Vector3.One);
                        }

                        vectorOutput[i] = vector;
                        break;
                    case ParticleFieldStorage.Integer:
                        scalarOutput[i] = parentScalars[index];
                        break;
                    case ParticleFieldStorage.Scalar:
                        var scalar = parentScalars[index] * scale;

                        if (fieldOutput.IsAlpha())
                        {
                            scalar = MathUtils.Saturate(scalar);
                        }

                        scalarOutput[i] = scalar;
                        break;
                }

                if (writeParentIndex)
                {
                    parentIndices[i] = index;
                    parentIds[i] = parentParticleIds[index];
                }

                runningIndex += increment;
            }
        }
    }
}
