using System.Collections.ObjectModel;

namespace ValveResourceFormat.Source1.Material;

internal sealed class VmtNode
{
    private static readonly IReadOnlyList<VmtNode> EmptyChildren = Array.AsReadOnly(Array.Empty<VmtNode>());
    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public string Name { get; }
    public string? Value { get; }
    public bool IsBlock => Value is null;
    public IReadOnlyList<VmtNode> Children { get; }
    public IReadOnlyDictionary<string, string> Values { get; }

    internal VmtNode(string name, string? value, IEnumerable<VmtNode> children)
    {
        Name = name;
        Value = value;

        if (value is not null)
        {
            Children = EmptyChildren;
            Values = EmptyValues;
            return;
        }

        Children = new List<VmtNode>(children).AsReadOnly();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in Children)
        {
            if (!child.IsBlock)
            {
                values[child.Name] = child.Value!;
            }
        }

        Values = new ReadOnlyDictionary<string, string>(values);
    }

    public VmtNode? GetBlock(string name)
    {
        foreach (var child in Children)
        {
            if (child.IsBlock && child.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }
}

/// <summary>
/// A material proxy of the <c>Proxies</c> block, with the values it is configured with.
/// </summary>
/// <param name="Name">The name of the proxy.</param>
/// <param name="Keys">The values of the proxy by case-insensitive key.</param>
internal sealed record VmtProxy(string Name, IReadOnlyDictionary<string, string> Keys);
