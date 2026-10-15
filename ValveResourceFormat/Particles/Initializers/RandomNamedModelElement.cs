using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives each new particle one of a list of named sequences, body parts or mesh groups of a
    /// model, picked at random, in list order, or from a shuffled bag that is reshuffled whenever it
    /// runs out. The pick is written as a number: a sequence or body part index, or a mesh group's bit
    /// in a mesh group mask.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomNamedModelSequence">C_INIT_RandomNamedModelSequence</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomNamedModelBodyPart">C_INIT_RandomNamedModelBodyPart</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomNamedModelMeshGroup">C_INIT_RandomNamedModelMeshGroup</seealso>
    class RandomNamedModelElement : ParticleFunctionInitializer
    {
        private readonly ModelElementKind kind;
        private readonly ModelReference model;
        private readonly string[] names;
        private readonly bool shuffle;
        private readonly bool linear;
        private readonly ParticleField fieldOutput = ParticleField.SequenceNumber;

        private int[] indices = [];
        private int next;

        public RandomNamedModelElement(ParticleDefinitionParser parse, ModelElementKind kind) : base(parse)
        {
            this.kind = kind;
            model = new ModelReference(parse);
            names = parse.Data.GetArray<string>("m_names") ?? [];
            shuffle = parse.Boolean("m_bShuffle", shuffle);
            linear = parse.Boolean("m_bLinear", linear);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            indices = ModelElements.Resolve(kind, names, model.Resolve(particleSystemState), particleSystemState.Data?.FileLoader);
            next = 0;

            if (shuffle)
            {
                Shuffle(particleSystemState);
            }
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            int value;

            if (shuffle || linear)
            {
                if (next >= indices.Length)
                {
                    if (shuffle)
                    {
                        Shuffle(particleSystemState);
                    }

                    next = 0;
                }

                value = indices.Length > 0 ? indices[next++] : 0;
            }
            else if (indices.Length <= 1)
            {
                value = indices.Length > 0 ? indices[0] : 0;
            }
            else
            {
                value = indices[(int)(indices.Length * particleSystemState.Random.Next())];
            }

            particle.SetScalar(fieldOutput, value);
            return particle;
        }

        private void Shuffle(ParticleSystemState particleSystemState)
        {
            for (var i = indices.Length - 1; i >= 1; i--)
            {
                var j = (int)((i + 1) * particleSystemState.Random.Next());
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
        }
    }
}
