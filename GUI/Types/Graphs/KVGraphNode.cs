using System.Globalization;
using System.Text;
using GUI.Types.Graphs.Core;
using ValveKeyValue;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph node carrying its source <see cref="KVObject"/> and exposing the Name/NodeType
/// naming used by the resource graph frontends.
/// </summary>
class KVGraphNode : GraphNode
{
    public KVObject? Data { get; set; }

    public KVGraphNode(KVObject? data)
    {
        Data = data;
    }

    public string? Name
    {
        get => Title;
        set => Title = value ?? string.Empty;
    }

    public string NodeType
    {
        get => Subtitle ?? string.Empty;
        set => Subtitle = value;
    }

    /// <summary>Single-line display form of a KV value.</summary>
    internal static string StringifyValue(KVObject obj)
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
