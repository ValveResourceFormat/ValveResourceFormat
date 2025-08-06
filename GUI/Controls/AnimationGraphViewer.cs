
using GUI.Utils;
using NodeGraphControl;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers;

internal class AnimationGraphViewer : NodeGraphControl.NodeGraphControl
{
    private VrfGuiContext vrfGuiContext;
    private KVObject animationGraphDefinition;

    public AnimationGraphViewer(VrfGuiContext guiContext, KVObject data)
    {
        vrfGuiContext = guiContext;
        animationGraphDefinition = data;
    }
}
