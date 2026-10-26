using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies one attribute from the parent system's particles to this system's particles every step,
    /// scaled for scalar and vector fields and copied as is for integer fields and bone indices. The
    /// walk over the parent list continues where the previous step left off, so with a different
    /// particle count on each side the parent a particle copies from shifts from step to step. A random
    /// distribution picks a new parent for every particle every step instead. Color and alpha clamp to
    /// [0, 1].
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InheritFromParentParticles">C_OP_InheritFromParentParticles</seealso>
    class InheritFromParentParticles : ParticleFunctionOperator
    {
        private readonly float scale = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int increment = 1;
        private readonly bool randomDistribution;

        private int parentIndex;

        public InheritFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.Float("m_flScale", scale);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            increment = parse.Int32("m_nIncrement", increment);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
        }

        public override void Reset()
        {
            parentIndex = 0;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;

            if (parentData == null || parentData.CurrentParticles.IsEmpty)
            {
                return;
            }

            var current = particles.Current;
            var parentParticles = parentData.CurrentParticles;
            var parentCount = parentParticles.Length;

            var storage = ParticleBuffer.StorageOf(fieldOutput);
            var copiesBoneIndices = storage == ParticleFieldStorage.BoneIndices;
            var copiesVectors = storage == ParticleFieldStorage.Vector;
            var copiesScalars = storage is ParticleFieldStorage.Integer or ParticleFieldStorage.Scalar;

            var boneIndices = copiesBoneIndices ? current.BoneIndices : default;
            var parentBoneIndices = copiesBoneIndices ? parentParticles.ReadBoneIndices() : default;
            var vectors = copiesVectors ? current.Vectors(fieldOutput) : default;
            var parentVectors = copiesVectors ? parentParticles.ReadVectors(fieldOutput) : default;
            var scalars = copiesScalars ? current.Floats(fieldOutput) : default;
            var parentScalars = copiesScalars ? parentParticles.ReadFloats(fieldOutput) : default;

            // A field that is not copied still walks the parent list
            for (var i = 0; i < current.Length; i++)
            {
                var source = NextParentIndex(parentCount, particleSystemState);

                // A negative increment walks the index below zero, which reads other lanes of the first block and then
                // memory before the buffer; the particle is skipped here
                if ((uint)source >= (uint)parentCount)
                {
                    continue;
                }

                switch (storage)
                {
                    case ParticleFieldStorage.BoneIndices:
                        boneIndices[i] = parentBoneIndices[source];
                        break;
                    case ParticleFieldStorage.Vector:
                        vectors[i] = ScaleVector(parentVectors[source]);
                        break;
                    case ParticleFieldStorage.Integer:
                        scalars[i] = parentScalars[source];
                        break;
                    case ParticleFieldStorage.Scalar:
                        scalars[i] = ScaleScalar(parentScalars[source]);
                        break;
                }
            }
        }

        /// <summary>Scales a copied vector, clamping a normalized field to [0, 1].</summary>
        private Vector3 ScaleVector(Vector3 value)
        {
            var scaled = value * scale;

            if (fieldOutput.IsNormalizedField())
            {
                scaled = Vector3.Clamp(scaled, Vector3.Zero, Vector3.One);
            }

            return scaled;
        }

        /// <summary>Scales a copied scalar, clamping alpha to [0, 1].</summary>
        private float ScaleScalar(float value)
        {
            var scaled = value * scale;

            if (fieldOutput.IsAlpha())
            {
                scaled = ParticleMath.ClampToUnit(scaled);
            }

            return scaled;
        }

        /// <summary>
        /// Returns the parent particle the next particle copies from, then moves the walk on by the
        /// increment. The result is outside the parent list when a negative increment walked off its start.
        /// </summary>
        private int NextParentIndex(int parentCount, ParticleSystemState particleSystemState)
        {
            if (randomDistribution)
            {
                parentIndex = particleSystemState.Random.NextIndex(parentCount);
            }
            else if (parentIndex > parentCount - 1)
            {
                parentIndex = 0;
            }

            var index = parentIndex;
            parentIndex += increment;

            return index;
        }
    }
}
