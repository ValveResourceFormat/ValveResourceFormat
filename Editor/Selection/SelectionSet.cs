using System.Threading;
using ValveResourceFormat.Renderer;

namespace ValveResourceFormat.Editor.Selection;

/// <summary>
/// The scene nodes selected in a viewport. It is changed from both the UI thread and the render
/// thread, so changes take a lock and readers iterate <see cref="Nodes"/>, an immutable snapshot.
/// </summary>
public sealed class SelectionSet
{
    private readonly Lock changeLock = new();
    private SceneNode[] nodes = [];

    /// <summary>Gets the selected nodes, in the order they were selected.</summary>
    public IReadOnlyList<SceneNode> Nodes => Volatile.Read(ref nodes);

    /// <summary>Gets whether nothing is selected.</summary>
    public bool IsEmpty => Volatile.Read(ref nodes).Length == 0;

    /// <summary>Replaces the selection with a single node.</summary>
    /// <param name="node">The node to select, or <see langword="null"/> to clear the selection.</param>
    public void Select(SceneNode? node)
    {
        using var _ = changeLock.EnterScope();

        foreach (var selected in nodes)
        {
            selected.IsSelected = false;
        }

        if (node == null)
        {
            Volatile.Write(ref nodes, []);
            return;
        }

        node.IsSelected = true;
        Volatile.Write(ref nodes, [node]);
    }

    /// <summary>Clears the selection.</summary>
    public void Clear() => Select(null);

    /// <summary>Adds a node to the selection, or removes it when it is already selected.</summary>
    /// <param name="node">The node to toggle.</param>
    public void Toggle(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var _ = changeLock.EnterScope();

        var index = Array.IndexOf(nodes, node);

        if (index >= 0)
        {
            node.IsSelected = false;
            Volatile.Write(ref nodes, [.. nodes.AsSpan(0, index), .. nodes.AsSpan(index + 1)]);
            return;
        }

        node.IsSelected = true;
        Volatile.Write(ref nodes, [.. nodes, node]);
    }

    /// <summary>Flips whether each selected node's layer is enabled, hiding or showing the selection.</summary>
    public void ToggleLayerEnabled()
    {
        foreach (var node in Nodes)
        {
            node.LayerEnabled = !node.LayerEnabled;
        }
    }
}
