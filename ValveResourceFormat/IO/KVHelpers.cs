using ValveKeyValue;

namespace ValveResourceFormat.IO;

internal class KVHelpers
{
    internal static KVObject MakeNode(string className, KVObject @object)
    {
        var node = KVObject.Collection();
        node.Add("_class", className);

        foreach (var (key, child) in @object)
        {
            if (key == "_class")
            {
                continue;
            }

            node.Add(key, child);
        }

        return node;
    }
    internal static KVObject MakeNode(string className, params (string Name, KVObject Value)[] properties)
    {
        var node = KVObject.Collection();
        node.Add("_class", className);
        foreach (var prop in properties)
        {
            node.Add(prop.Name, prop.Value);
        }
        return node;
    }

    internal static (KVObject Node, KVObject Children) MakeListNode(string className, string containerName = "children")
    {
        var children = KVObject.Array();
        var node = MakeNode(className, (containerName, children));
        return (node, children);
    }

    internal static KVObject MakeArray(params KVObject[] values)
    {
        var arr = KVObject.Array();
        foreach (var v in values)
        {
            arr.Add(v);
        }
        return arr;
    }

    internal static KVObject MakeArray(IEnumerable<KVObject> values)
    {
        var arr = KVObject.Array();
        foreach (var v in values)
        {
            arr.Add(v);
        }
        return arr;
    }

    internal static KVObject ToKVArray(Vector3 v)
        => MakeArray(v.X, v.Y, v.Z);

    internal static KVObject ToKVArray(Quaternion q)
        => MakeArray(q.X, q.Y, q.Z, q.W);
}
