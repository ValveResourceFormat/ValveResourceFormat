using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Graphs;

internal class PulseGraphViewer : GLGraphViewer
{
    public PulseGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        var builder = new PulseGraphBuilder(data)
        {
            ProgressReporter = new Progress<string>(message => Log.Debug(nameof(PulseGraphViewer), message)),
        };

        builder.Build(View.Document);
    }
}
