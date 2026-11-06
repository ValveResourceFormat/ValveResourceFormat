using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1;

/// <summary>Lists the resources a converted resource refers to as its external references, as compiled resources do.</summary>
internal static class Source1ResourceReferences
{
    /// <summary>
    /// Adds the strings marked as resource names in the data blocks of a resource to its external references,
    /// which are created when it has none. Names that are already listed are kept as they are.
    /// </summary>
    /// <param name="resource">The resource to list the references of.</param>
    public static void Update(Resource resource)
    {
        var references = GetReferences(resource);
        var names = GetNames(references);

        foreach (var block in resource.Blocks)
        {
            switch (block)
            {
                case ResourceTypes.Material material:
                    foreach (var texturePath in material.TextureParams.Values)
                    {
                        Add(references, names, texturePath);
                    }

                    break;
                case KeyValuesOrNTRO keyValues when keyValues.Data != null:
                    Collect(keyValues.Data, references, names);
                    break;
                case BinaryKV3 binaryKeyValues:
                    Collect(binaryKeyValues.Data.Root, references, names);
                    break;
            }
        }
    }

    /// <summary>Creates the value of a key that names a resource, which <see cref="Update"/> lists.</summary>
    /// <param name="path">The path of the resource without the compiled suffix.</param>
    public static KVObject Reference(string path) => ResourceTypes.Model.CreateResourceReference(path);

    /// <summary>
    /// Adds a name to the external references of a resource, which are created when it has none.
    /// </summary>
    /// <param name="resource">The resource that refers to the name.</param>
    /// <param name="name">The path of the referenced resource without the compiled suffix.</param>
    public static void Add(Resource resource, string name)
    {
        var references = GetReferences(resource);
        var names = GetNames(references);

        Add(references, names, name);
    }

    private static ResourceExtRefList GetReferences(Resource resource)
    {
        if (resource.ExternalReferences is { } existing)
        {
            return existing;
        }

        var references = new ResourceExtRefList { Resource = resource };
        resource.AddBlock(references);

        return references;
    }

    private static HashSet<string> GetNames(ResourceExtRefList references)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references.ResourceRefInfoList)
        {
            names.Add(reference.Name);
        }

        return names;
    }

    private static void Add(ResourceExtRefList references, HashSet<string> names, string name)
    {
        if (name.Length == 0 || !names.Add(name))
        {
            return;
        }

        references.ResourceRefInfoList.Add(new ResourceExtRefList.ResourceReferenceInfo { Name = name });
    }

    private static void Collect(KVObject value, ResourceExtRefList references, HashSet<string> names)
    {
        switch (value.ValueType)
        {
            case KVValueType.Collection:
                foreach (var (_, child) in value.Children)
                {
                    Collect(child, references, names);
                }

                break;

            case KVValueType.Array:
                foreach (var element in value.Elements)
                {
                    Collect(element, references, names);
                }

                break;

            case KVValueType.String:
                if (value.Flag == KVFlag.Resource)
                {
                    Add(references, names, (string)value);
                }

                break;
        }
    }
}
