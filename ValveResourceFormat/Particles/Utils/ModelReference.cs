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

            var modelPath = fromRenderer ? FirstRendererModel(simulation.Definition) : path;

            if (string.IsNullOrEmpty(modelPath))
            {
                return null;
            }

            return simulation.FileLoader.LoadFileCompiled(modelPath)?.DataBlock as Model;
        }

        private static string? FirstRendererModel(KVObject definition)
        {
            foreach (var renderer in definition.GetArray("m_Renderers") ?? [])
            {
                foreach (var entry in renderer.GetArray("m_ModelList") ?? [])
                {
                    var model = entry.GetStringProperty("m_model");

                    if (!string.IsNullOrEmpty(model))
                    {
                        return model;
                    }
                }
            }

            return null;
        }
    }
}
