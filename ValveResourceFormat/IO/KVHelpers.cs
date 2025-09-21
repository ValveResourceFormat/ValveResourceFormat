using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.IO;

internal class KVHelpers
{
    internal static KVObject MakeNode(string className, KVObject @object)
    {
        var node = new KVObject(className, @object.Properties.Count + 1);
        node.AddProperty("_class", className);

        foreach (var property in @object.Properties)
        {
            if (property.Key == "_class")
            {
                continue;
            }

            node.AddProperty(property.Key, property.Value);
        }

        return node;
    }
    internal static KVObject MakeNode(string className, params (string Name, object Value)[] properties)
    {
        var node = new KVObject(className, capacity: properties.Length + 1);
        node.AddProperty("_class", className);
        foreach (var prop in properties)
        {
            node.AddProperty(prop.Name, prop.Value);
        }
        return node;
    }

    internal static (KVObject Node, KVObject Children) MakeListNode(string className, string containerName = "children")
    {
        var children = new KVObject(null, isArray: true);
        var node = MakeNode(className, (containerName, children));
        return (node, children);
    }
}
