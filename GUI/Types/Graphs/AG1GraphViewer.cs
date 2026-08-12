using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for AG1 (Animgraph1) files, compiled or in the editor schema.
/// </summary>
internal class AG1GraphViewer : GLGraphViewer
{
    private readonly AnimGraph1Builder builder;

    public AG1GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        builder = new AnimGraph1Builder(data, vrfGuiContext)
        {
            ProgressReporter = new Progress<string>(message => Log.Debug(nameof(AG1GraphViewer), message)),
        };

        BuildGraph();
    }

    private void BuildGraph() => builder.Build(View.Document);

    protected override bool HasParameterWireToggle => builder.HasParameterConsumers;

    protected override void SetDrawParameterWires(bool draw)
    {
        if (builder.DrawParameterWires == draw)
        {
            return;
        }

        builder.DrawParameterWires = draw;
        View.Rebuild(BuildGraph);
        RefreshStatsLabel();
        RefitToGraph();
    }

    public override void Dispose()
    {
        builder.Dispose();

        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
