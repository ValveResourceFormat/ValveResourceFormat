using System.Globalization;
using System.Linq;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Synthetic graphs for <see cref="GraphLayoutLab"/>, one per documented cause of wire overlap,
/// so every improvement has a case that isolates what it is supposed to fix.
/// </summary>
internal static class GraphLayoutLabFixtures
{
    public static IEnumerable<(string Name, Func<GraphView> Build)> All =>
    [
        ("chain", Chain),
        ("hub-fanout", HubFanout),
        ("long-wires", LongWires),
        ("state-cycles", StateCycles),
        ("wide-rank", WideRank),
        ("crossbar", Crossbar),
        ("mixed-islands", MixedIslands),
    ];

    // Tall cards in a straight run. Aligning node centers leaves every wire on a slight
    // diagonal; aligning socket pivots should make the whole chain one horizontal line.
    private static GraphView Chain()
    {
        var view = new GraphView();
        GraphNode? previous = null;

        for (var i = 0; i < 8; i++)
        {
            var node = view.AddNode(new GraphNode { Title = $"Stage {i}", Category = GraphHue.Teal });

            // Padding rows of varying count push the real socket well off the card center.
            for (var pad = 0; pad < i % 4 + 1; pad++)
            {
                node.AddText($"detail {pad}");
            }

            var input = node.AddInput("in", GraphHue.Cyan);
            node.AddText("more detail");
            var output = node.AddOutput("out", GraphHue.Amber);

            if (previous != null)
            {
                view.Connect(previous.Outputs[^1], input);
            }

            previous = node;
            _ = output;
        }

        return view;
    }

    // One producer feeding many consumers from a single socket: every wire starts at the same
    // pixel with the same tangent.
    private static GraphView HubFanout()
    {
        var view = new GraphView();
        var hub = view.AddNode(new GraphNode { Title = "Variable hub", Category = GraphHue.Purple });
        var reads = hub.AddOutput("reads", GraphHue.Purple);
        var writes = hub.AddInput("writes", GraphHue.Purple);

        for (var i = 0; i < 22; i++)
        {
            var reader = view.AddNode(new GraphNode { Title = $"Reader {i}", Category = GraphHue.Green });
            view.Connect(reads, reader.AddInput("value", GraphHue.Amber), dashed: true);
        }

        for (var i = 0; i < 6; i++)
        {
            var writer = view.AddNode(new GraphNode { Title = $"Writer {i}", Category = GraphHue.Orange });
            view.Connect(writer.AddOutput("set", GraphHue.Amber), writes, dashed: true);
        }

        return view;
    }

    // A source wired directly into every later rank, so its wires fly over the ranks between.
    private static GraphView LongWires()
    {
        var view = new GraphView();
        var source = view.AddNode(new GraphNode { Title = "Source", Category = GraphHue.Emerald });
        var stages = new List<GraphNode>();

        for (var i = 0; i < 7; i++)
        {
            var node = view.AddNode(new GraphNode { Title = $"Rank {i + 1}", Category = GraphHue.Blue });
            node.AddInput("chain", GraphHue.Cyan);
            node.AddInput("direct", GraphHue.Amber);
            node.AddOutput("out", GraphHue.Cyan);
            stages.Add(node);
        }

        for (var i = 0; i < stages.Count; i++)
        {
            view.Connect(source.AddOutput($"tap {i}", GraphHue.Amber), stages[i].Inputs[1]);

            if (i > 0)
            {
                view.Connect(stages[i - 1].Outputs[0], stages[i].Inputs[0]);
            }
        }

        return view;
    }

    // A transition ring: the edge closing the loop is the cycle breaker the ranking reverses.
    private static GraphView StateCycles()
    {
        var view = new GraphView();
        var states = new List<GraphNode>();

        for (var i = 0; i < 6; i++)
        {
            var state = view.AddNode(new GraphNode { Title = $"State {i}", Category = GraphHue.Indigo });
            state.AddInput("From", GraphHue.Magenta);
            state.AddOutput("Transitions", GraphHue.Magenta);
            states.Add(state);
        }

        for (var i = 0; i < states.Count; i++)
        {
            var next = states[(i + 1) % states.Count];
            view.Connect(states[i].Outputs[0], next.Inputs[0], dashed: true);
        }

        // A couple of long shortcuts back, plus a self transition.
        view.Connect(states[4].Outputs[0], states[1].Inputs[0], dashed: true);
        view.Connect(states[3].Outputs[0], states[0].Inputs[0], dashed: true);
        view.Connect(states[2].Outputs[0], states[2].Inputs[0], dashed: true);

        return view;
    }

    // Far more nodes in one rank than fit a column, which is what forces the wrap.
    private static GraphView WideRank()
    {
        var view = new GraphView();
        var sinks = new List<GraphNode>();

        for (var i = 0; i < 3; i++)
        {
            var sink = view.AddNode(new GraphNode { Title = $"Collector {i}", Category = GraphHue.Red });
            sink.AddInput("events", GraphHue.Cyan);
            sinks.Add(sink);
        }

        for (var i = 0; i < 64; i++)
        {
            var source = view.AddNode(new GraphNode { Title = $"Trigger {i:D2}", Category = GraphHue.Olive });
            view.Connect(source.AddOutput("OnFire", GraphHue.Orange), sinks[i % sinks.Count].Inputs[0]);
        }

        return view;
    }

    // Two ranks wired back to front, so the shipped ordering leaves a full crossbar and both
    // the ordering repair and the socket permutation have something to untangle.
    private static GraphView Crossbar()
    {
        var view = new GraphView();
        var left = new List<GraphNode>();
        var right = new List<GraphNode>();

        for (var i = 0; i < 12; i++)
        {
            var producer = view.AddNode(new GraphNode { Title = $"Out {i:D2}", Category = GraphHue.Amber });
            var consumer = view.AddNode(new GraphNode { Title = $"In {i:D2}", Category = GraphHue.Cyan });
            left.Add(producer);
            right.Add(consumer);
        }

        for (var i = 0; i < left.Count; i++)
        {
            var target = right[left.Count - 1 - i];

            for (var k = 0; k < 3; k++)
            {
                var output = left[i].AddOutput($"o{k}", GraphHue.Amber);
                var input = right[(i + k) % right.Count].AddInput($"i{i}{k}", GraphHue.Cyan);
                view.Connect(output, input);
            }

            _ = target;
        }

        foreach (var node in left.Concat(right))
        {
            node.PairSocketRows();
            node.SocketOrderFixed = false;
        }

        return view;
    }

    // Several disconnected islands of different shapes, the way an entity I/O graph arrives.
    private static GraphView MixedIslands()
    {
        var view = new GraphView();

        for (var island = 0; island < 5; island++)
        {
            var root = view.AddNode(new GraphNode
            {
                Title = $"logic_relay_{island:D2}",
                Subtitle = "logic_relay",
                Category = GraphHue.Orange,
            });

            var fire = root.AddOutput("OnTrigger", GraphHue.Orange);
            root.AddInput("Trigger", GraphHue.Cyan);

            for (var child = 0; child < 3 + island; child++)
            {
                var target = view.AddNode(new GraphNode
                {
                    Title = $"door_{island:D2}_{child:D2}",
                    Subtitle = "prop_door_rotating",
                    Category = GraphHue.Teal,
                });

                var open = target.AddInput("Open", GraphHue.Cyan);
                var opened = target.AddOutput("OnFullyOpen", GraphHue.Orange);

                view.Connect(fire, open, label: (child * 0.25f).ToString("0.##s", CultureInfo.InvariantCulture));

                // Every other child reports back, closing a cycle through the relay.
                if (child % 2 == 0)
                {
                    view.Connect(opened, root.Inputs[0]);
                }
            }

            foreach (var node in view.Nodes)
            {
                node.PairSocketRows();
                node.SocketOrderFixed = false;
            }
        }

        return view;
    }
}
