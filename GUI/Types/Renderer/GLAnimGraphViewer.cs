using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace GUI.Types.Renderer
{
    class GLAnimGraphViewer : GLModelViewer
    {
        public GLAnimGraphViewer(VrfGuiContext guiContext, AnimGraph animGraph) : base(guiContext)
        {
            var animGraphAssociatedModel = animGraph.Data.GetProperty<string>("m_modelName");
            var modelResource = guiContext.LoadFileCompiled(animGraphAssociatedModel) ?? guiContext.LoadFileCompiled("models/dev/error.vmdl");
            model = (Model)modelResource?.DataBlock;
        }
    }
}
