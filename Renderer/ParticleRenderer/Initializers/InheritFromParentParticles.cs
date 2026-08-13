using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Copies one attribute from a parent particle to each new child particle, scaled for scalar and
    /// vector fields, walking the parent list with a running index that resets to zero past the end
    /// rather than wrapping modulo. Color and alpha class fields clamp to [0, 1] after scaling.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InheritFromParentParticles">C_INIT_InheritFromParentParticles</seealso>
    class InheritFromParentParticles : ParticleFunctionInitializer
    {
        private readonly float scale = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int increment = 1;
        private readonly bool randomDistribution;
        private readonly int randomSeed;

        /// <summary>The parent particle index attribute is only written from behavior version 9.</summary>
        private readonly bool writeParentIndex;

        private int runningIndex;
        private int randomCounter;

        public InheritFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.Float("m_flScale", scale);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
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

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var parentData = particleSystemState.ParentSystem?.Data;
            if (parentData == null)
            {
                return particle;
            }

            var parentParticles = parentData.CurrentParticles;
            var count = parentParticles.Length;
            if (count == 0)
            {
                return particle;
            }

            int index;
            if (randomDistribution)
            {
                // A non-zero authored seed drives a counter private to this initializer, leaving the
                // system's shared draw sequence untouched
                var fraction = randomSeed != 0
                    ? ParticleRandom.ForSample(particleSystemState.Random.Seed + randomSeed + randomCounter++)
                    : particleSystemState.Random.Next();
                index = Math.Clamp((int)(count * fraction), 0, count - 1);
                runningIndex = index;
            }
            else if (runningIndex > count - 1)
            {
                runningIndex = 0;
                index = 0;
            }
            else
            {
                index = runningIndex;
            }

            ref var parent = ref parentParticles[index];

            if (fieldOutput.FieldType() == "vector")
            {
                var value = parent.GetVector(fieldOutput) * scale;

                if (fieldOutput is ParticleField.Color or ParticleField.GlowRgb)
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                particle.SetVector(fieldOutput, value);
            }
            else
            {
                var value = parent.GetScalar(fieldOutput) * scale;

                if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
                {
                    value = Math.Clamp(value, 0f, 1f);
                }

                particle.SetScalar(fieldOutput, value);
            }

            if (writeParentIndex)
            {
                particle.ParentParticleIndex = index;
            }

            runningIndex += increment;

            return particle;
        }
    }
}
