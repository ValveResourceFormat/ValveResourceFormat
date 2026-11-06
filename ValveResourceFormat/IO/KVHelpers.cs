using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

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

    internal static KVObject ToKVArray(Vector4 v)
        => MakeArray(v.X, v.Y, v.Z, v.W);

    internal static KVObject ToKVArray(Quaternion q)
        => MakeArray(q.X, q.Y, q.Z, q.W);

    /// <summary>Writes the first three columns of a matrix with four rows each, like <c>m_invBindPose</c>.</summary>
    internal static KVObject ToKVArray(Matrix4x4 m)
        => MakeArray(
            m.M11, m.M21, m.M31, m.M41,
            m.M12, m.M22, m.M32, m.M42,
            m.M13, m.M23, m.M33, m.M43);

    internal static void AddIfPresent(KVObject target, string targetKey, KVObject? source, string sourceKey)
    {
        if (source is null || !source.TryGetValue(sourceKey, out var value))
        {
            return;
        }

        target.Add(targetKey, value);
    }
}
