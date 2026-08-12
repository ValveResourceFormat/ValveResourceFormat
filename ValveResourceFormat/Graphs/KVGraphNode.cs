using System.Globalization;
using System.Text;
using ValveKeyValue;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Graph node carrying its source <see cref="KVObject"/> and exposing the Name/NodeType
/// naming used by the resource graph frontends.
/// </summary>
public class KVGraphNode : GraphNode
{
    /// <summary>The keyvalues object this node was built from.</summary>
    public KVObject? Data { get; set; }

    /// <summary>Creates a node carrying its source keyvalues object.</summary>
    /// <param name="data">The object this node was built from.</param>
    public KVGraphNode(KVObject? data)
    {
        Data = data;
    }

    /// <summary>The node's title, under the name the resource graph frontends use.</summary>
    public string? Name
    {
        get => Title;
        set => Title = value ?? string.Empty;
    }

    /// <summary>The node's subtitle, under the name the resource graph frontends use.</summary>
    public string NodeType
    {
        get => Subtitle ?? string.Empty;
        set => Subtitle = value;
    }

    /// <summary>Single-line display form of a KV value.</summary>
    /// <param name="obj">The value to render.</param>
    public static string StringifyValue(KVObject obj)
    {
        switch (obj.ValueType)
        {
            case KVValueType.String:
                return $"\"{obj}\"";
            case KVValueType.Boolean:
                return obj.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false";
            case KVValueType.Array:
                {
                    var list = obj.AsArraySpan();
                    StringBuilder sb = new();
                    sb.Append('[');
                    var firstElem = true;
                    foreach (var elem in list)
                    {
                        if (!firstElem)
                        {
                            sb.Append(", ");
                        }
                        firstElem = false;

                        sb.Append(StringifyValue(elem));
                    }
                    sb.Append(']');
                    return sb.ToString();
                }
            case KVValueType.Int16:
            case KVValueType.UInt16:
            case KVValueType.Int32:
            case KVValueType.UInt32:
            case KVValueType.Int64:
            case KVValueType.UInt64:
            case KVValueType.FloatingPoint:
            case KVValueType.FloatingPoint64:
            default:
                return obj.ToString(CultureInfo.InvariantCulture);
        }
    }
}
