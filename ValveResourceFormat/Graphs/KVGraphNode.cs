using System.Globalization;
using System.Text;
using ValveKeyValue;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Graph node exposing the Name/NodeType naming used by the resource graph frontends.
/// </summary>
internal class KVGraphNode : GraphNode
{
    /// <summary>The node's title, under the name the resource graph frontends use.</summary>
    public string Name
    {
        get => Title;
        set => Title = value;
    }

    /// <summary>The node's subtitle, under the name the resource graph frontends use.</summary>
    public string NodeType
    {
        get => Subtitle ?? string.Empty;
        set => Subtitle = value;
    }

    /// <summary>Single-line display form of a KV value.</summary>
    /// <param name="obj">The value to render.</param>
    public static string StringifyValue(KVObject obj) => obj.ValueType switch
    {
        KVValueType.String => $"\"{obj}\"",
        KVValueType.Boolean => obj.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false",
        KVValueType.Array => StringifyArray(obj),
        _ => obj.ToString(CultureInfo.InvariantCulture),
    };

    private static string StringifyArray(KVObject array)
    {
        var sb = new StringBuilder("[");
        var first = true;

        foreach (var element in array.AsArraySpan())
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(StringifyValue(element));
        }

        return sb.Append(']').ToString();
    }
}
