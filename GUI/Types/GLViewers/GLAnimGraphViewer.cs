using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace GUI.Types.GLViewers
{
    class GLAnimGraphViewer : GLModelViewer
    {
        public GLAnimGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, AnimGraph animGraph) : base(vrfGuiContext, rendererContext)
        {
            var animGraphAssociatedModel = animGraph.Data.GetProperty<string>("m_modelName");
            var modelResource = rendererContext.FileLoader.LoadFileCompiled(animGraphAssociatedModel) ?? rendererContext.FileLoader.LoadFileCompiled("models/dev/error.vmdl");
            model = (Model)modelResource?.DataBlock;
        }
    }
}
