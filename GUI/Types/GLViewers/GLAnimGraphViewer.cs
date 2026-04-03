using System.IO;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    class GLAnimGraphViewer : GLModelViewer
    {
        public GLAnimGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, AnimGraph animGraph) : base(vrfGuiContext, rendererContext)
        {
            var animGraphAssociatedModel = animGraph.Data.GetStringProperty("m_modelName");
            var modelResource = rendererContext.FileLoader.LoadFileCompiled(animGraphAssociatedModel) ?? rendererContext.FileLoader.LoadFileCompiled("models/dev/error.vmdl");

            if (modelResource?.DataBlock is not Model model)
            {
                throw new InvalidDataException($"AnimGraph associated model is not a valid model.");
            }
        }
    }
}
