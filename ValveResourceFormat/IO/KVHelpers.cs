using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

internal class KVHelpers
{
    internal static KVObject MakeNode(string className, KVObject @object)
    {
        var node = new KVObject(className);
        node.AddProperty("_class", (KVValue)className);

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
        node.AddProperty("_class", (KVValue)className);
        foreach (var prop in properties)
        {
            node.AddProperty(prop.Name, prop.Value);
        }
        return node;
    }

    internal static (KVObject Node, KVObject Children) MakeListNode(string className, string containerName = "children")
    {
        var children = new KVObject(null, Array.Empty<KVValue>());
        var node = MakeNode(className, (containerName, children.Value));
        return (node, children);
    }

    internal static KVValue MakeArray(params KVValue[] values)
    {
        var arr = new KVArrayValue();
        arr.AddRange(values);
        return arr;
    }

    internal static KVValue MakeArray(IEnumerable<KVValue> values)
    {
        var arr = new KVArrayValue();
        arr.AddRange(values);
        return arr;
    }

    internal static KVValue MakeArray(KVObject[] objects)
    {
        var arr = new KVArrayValue();
        for (var i = 0; i < objects.Length; i++)
        {
            arr.Add(objects[i].Value);
        }
        return arr;
    }

    internal static KVValue ToKVValue(Vector3 v)
        => MakeArray((KVValue)v.X, (KVValue)v.Y, (KVValue)v.Z);

    internal static KVValue ToKVValue(Quaternion q)
        => MakeArray((KVValue)q.X, (KVValue)q.Y, (KVValue)q.Z, (KVValue)q.W);
}
