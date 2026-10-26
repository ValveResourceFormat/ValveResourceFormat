using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The model a function names in <c>m_hModel</c>, or with <c>m_bModelFromRenderer</c> the first
    /// model the system's renderers draw, loaded through the running system.
    /// </summary>
    internal sealed class ModelReference
    {
        private readonly string path;
        private readonly bool fromRenderer;

        public ModelReference(ParticleDefinitionParser parse)
        {
            path = parse.String("m_hModel");
            fromRenderer = parse.Boolean("m_bModelFromRenderer");
        }

        /// <summary>The model, or null when none is named or it does not load.</summary>
        public Model? Resolve(ParticleSystemState systemState)
        {
            if (systemState.Data is not { } simulation)
            {
                return null;
            }

            var modelPath = fromRenderer ? FirstRendererModel(simulation.Definition, path) : path;

            if (string.IsNullOrEmpty(modelPath))
            {
                return null;
            }

            return simulation.FileLoader.LoadFileCompiled(modelPath)?.DataBlock as Model;
        }

        /// <summary>
        /// Only the first renderer counts, through its first model entry. A system without renderers
        /// keeps the named model.
        /// </summary>
        private static string? FirstRendererModel(KVObject definition, string namedModel)
        {
            var renderers = definition.GetArray("m_Renderers");

            if (renderers is not { Count: > 0 })
            {
                return namedModel;
            }

            var models = renderers[0].GetArray("m_ModelList");

            if (models is not { Count: > 0 })
            {
                return null;
            }

            return models[0].GetStringProperty("m_model");
        }
    }
}
