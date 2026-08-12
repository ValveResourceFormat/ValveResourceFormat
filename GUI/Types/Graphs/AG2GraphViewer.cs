using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveResourceFormat.Graphs;
using ValveKeyValue;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Graphs;

internal class AG2GraphViewer : GLGraphViewer
{
    private readonly NmGraphBuilder builder;

    public AG2GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        builder = new NmGraphBuilder(data)
        {
            ProgressReporter = new Progress<string>(message => Log.Debug(nameof(AG2GraphViewer), message)),
        };

        BuildGraph();
    }

    private void BuildGraph() => builder.Build(View.Document);

    protected override bool HasStateMachineToggle => true;

    protected override void SetDrawStateMachines(bool draw)
    {
        if (builder.DrawStateMachines == draw)
        {
            return;
        }

        builder.DrawStateMachines = draw;
        RebuildGraph();
    }

    protected override bool HasParameterWireToggle => builder.HasControlParameters;

    protected override void SetDrawParameterWires(bool draw)
    {
        if (builder.DrawParameterWires == draw)
        {
            return;
        }

        builder.DrawParameterWires = draw;
        RebuildGraph();
    }

    private void RebuildGraph()
    {
        View.Rebuild(BuildGraph);
        RefreshStatsLabel();
        RefitToGraph();
    }
}
