using ValveKeyValue;

namespace ValveResourceFormat.IO;

internal class KVHelpers
{
    internal static KVObject MakeNode(string className, KVObject @object)
    {
        var node = new KVObject(className);
        node.Add("_class", (KVValue)className);

        foreach (var child in @object.Children)
        {
            if (child.Name == "_class")
            {
                continue;
            }

            node.Add(child);
        }

        return node;
    }
    internal static KVObject MakeNode(string className, params (string Name, KVValue Value)[] properties)
    {
        var node = new KVObject(className);
        node.Add("_class", (KVValue)className);
        foreach (var prop in properties)
        {
            node.Add(prop.Name, prop.Value);
        }
        return node;
    }

    internal static (KVObject Node, KVObject Children) MakeListNode(string className, string containerName = "children")
    {
        var children = KVObject.Array(null);
        var node = MakeNode(className, (containerName, children.Value));
        return (node, children);
    }

    internal static KVValue MakeArray(params KVValue[] values)
    {
        var arr = KVObject.Array(null);
        foreach (var v in values)
        {
            arr.Add(v);
        }
        return arr.Value;
    }

    internal static KVValue MakeArray(IEnumerable<KVValue> values)
    {
        var arr = KVObject.Array(null);
        foreach (var v in values)
        {
            arr.Add(v);
        }
        return arr.Value;
    }

    internal static KVValue MakeArray(KVObject[] objects)
    {
        var arr = KVObject.Array(null);
        for (var i = 0; i < objects.Length; i++)
        {
            arr.Add(objects[i].Value);
        }
        return arr.Value;
    }

    internal static KVValue ToKVValue(Vector3 v)
        => MakeArray((KVValue)v.X, (KVValue)v.Y, (KVValue)v.Z);

    internal static KVValue ToKVValue(Quaternion q)
        => MakeArray((KVValue)q.X, (KVValue)q.Y, (KVValue)q.Z, (KVValue)q.W);
}
