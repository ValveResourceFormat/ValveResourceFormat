using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Maps a sequence, body part or mesh group through three lists of names of a model: the place
    /// of the held value in <c>m_inNames</c> selects the entry of <c>m_outNames</c>, and a value not
    /// listed takes a random <c>m_fallbackNames</c> entry when there are any.
    /// </summary>
    internal sealed class NamedModelElementRemap
    {
        private readonly ModelElementKind kind;
        private readonly ModelReference model;
        private readonly string[] inNames;
        private readonly string[] outNames;
        private readonly string[] fallbackNames;

        private int[] inIndices = [];
        private int[] outIndices = [];
        private int[] fallbackIndices = [];
        private (int Min, int Max) inRange;

        public NamedModelElementRemap(ParticleDefinitionParser parse, ModelElementKind kind)
        {
            this.kind = kind;
            model = new ModelReference(parse);
            inNames = parse.Data.GetArray<string>("m_inNames") ?? [];
            outNames = parse.Data.GetArray<string>("m_outNames") ?? [];
            fallbackNames = parse.Data.GetArray<string>("m_fallbackNames") ?? [];
        }

        /// <summary>Looks the names up in the model, which the system must be running to load.</summary>
        public void Resolve(ParticleSystemState particleSystemState)
        {
            var resolved = model.Resolve(particleSystemState);
            var fileLoader = particleSystemState.Data?.FileLoader;

            inIndices = ModelElements.Resolve(kind, inNames, resolved, fileLoader);
            outIndices = ModelElements.Resolve(kind, outNames, resolved, fileLoader);
            fallbackIndices = ModelElements.Resolve(kind, fallbackNames, resolved, fileLoader);
            inRange = ModelElements.Range(inIndices);
        }

        /// <summary>
        /// The value <paramref name="input"/> maps to. Without fallbacks a value below every listed
        /// one maps to the first output and one above them to the last.
        /// </summary>
        public int Remap(int input, ParticleSystemState particleSystemState)
        {
            int position;

            if (fallbackIndices.Length <= 0)
            {
                position = ModelElements.PlaceOf(inIndices, inRange, input, outIndices.Length - 1);
            }
            else
            {
                position = Array.IndexOf(inIndices, input);

                if (position < 0)
                {
                    return fallbackIndices.Length == 1
                        ? fallbackIndices[0]
                        : fallbackIndices[particleSystemState.Random.NextIndex(fallbackIndices.Length)];
                }
            }

            if (outIndices.Length <= 0)
            {
                return 0;
            }

            if (position < 0 || outIndices.Length == 1)
            {
                return outIndices[0];
            }

            return position < outIndices.Length ? outIndices[position] : outIndices[^1];
        }
    }
}
