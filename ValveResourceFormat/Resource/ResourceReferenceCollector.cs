using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat;

/// <summary>
/// Collects every file name a resource references, from all the blocks that can hold one.
/// </summary>
/// <remarks>
/// The external reference list only covers the references the engine resolves by id. Child and weak
/// references live in the edit info block, and a lot of resource types store their references as plain
/// names in their data instead, which is why this walks the data blocks as well.
/// </remarks>
public static class ResourceReferenceCollector
{
    /// <summary>
    /// Collects the references of a resource, merging names that appear in more than one place.
    /// </summary>
    /// <param name="resource">The resource to read.</param>
    /// <returns>The references, in the order they were found.</returns>
    public static IReadOnlyList<ResourceReference> Collect(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var collector = new Collector(resource.FileName);

        collector.CollectExternalReferences(resource.ExternalReferences);
        collector.CollectEditInfo(resource.EditInfo);

        foreach (var block in resource.Blocks)
        {
            collector.CollectBlock(block);
        }

        return collector.Build();
    }

    private sealed class Collector(string? selfFileName)
    {
        private const int MaxDepth = 64;
        private const int MaxNodes = 1_000_000;
        private const int MaxNameLength = 512;

        private readonly List<Entry> entries = [];
        private readonly Dictionary<string, int> entryIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? selfKey = selfFileName == null ? null : NormalizeKey(selfFileName);
        private int nodeBudget = MaxNodes;

        private sealed class Entry
        {
            public required string Name { get; init; }
            public required ResourceReferenceKind Kinds { get; set; }
            public required string? Source { get; set; }
            public required ulong Id { get; set; }
        }

        public List<ResourceReference> Build()
        {
            var references = new List<ResourceReference>(entries.Count);

            foreach (var entry in entries)
            {
                references.Add(ResourceReference.Create(entry.Name, entry.Kinds, entry.Source, entry.Id));
            }

            return references;
        }

        public void CollectExternalReferences(ResourceExtRefList? externalReferences)
        {
            if (externalReferences == null)
            {
                return;
            }

            foreach (var reference in externalReferences.ResourceRefInfoList)
            {
                Add(reference.Name, ResourceReferenceKind.External, id: reference.Id);
            }
        }

        public void CollectEditInfo(ResourceEditInfo? editInfo)
        {
            if (editInfo == null)
            {
                return;
            }

            foreach (var child in editInfo.ChildResourceList)
            {
                Add(child, ResourceReferenceKind.Child);
            }

            foreach (var related in editInfo.AdditionalRelatedFiles)
            {
                Add(related.ContentRelativeFilename, ResourceReferenceKind.Related);
            }

            foreach (var dependency in editInfo.InputDependencies)
            {
                Add(dependency.ContentRelativeFilename, ResourceReferenceKind.InputDependency);
            }

            foreach (var dependency in editInfo.AdditionalInputDependencies)
            {
                Add(dependency.ContentRelativeFilename, ResourceReferenceKind.InputDependency);
            }

            if (editInfo is not ResourceEditInfo2 editInfo2)
            {
                return;
            }

            foreach (var weak in editInfo2.WeakReferenceList)
            {
                Add(weak, ResourceReferenceKind.Weak);
            }

            if (editInfo2.SubassetReferences == null)
            {
                return;
            }

            foreach (var (subassetType, subassets) in editInfo2.SubassetReferences)
            {
                foreach (var subasset in subassets.Keys)
                {
                    Add(subasset, ResourceReferenceKind.Subasset, subassetType);
                }
            }
        }

        public void CollectBlock(Block block)
        {
            switch (block)
            {
                // Both are read above, into references of their own
                case ResourceExtRefList:
                case ResourceEditInfo:
                    break;

                case Panorama panorama:
                    foreach (var image in panorama.Images)
                    {
                        Add(image.Name, ResourceReferenceKind.PanoramaImage, $"{image.Width}x{image.Height}");
                    }

                    break;

                case ResourceManifest manifest:
                    foreach (var resources in manifest.Resources)
                    {
                        foreach (var name in resources)
                        {
                            Add(name, ResourceReferenceKind.Manifest);
                        }
                    }

                    break;

                case ResponseRules responseRules:
                    foreach (var include in responseRules.Includes)
                    {
                        Add(include.Name, ResourceReferenceKind.Data, "include");
                    }

                    break;

                case KeyValuesOrNTRO keyValues:
                    WalkKeyValues(keyValues.Data, null, 0);
                    break;

                case BinaryKV3 kv3:
                    WalkKeyValues(kv3.Data?.Root, null, 0);
                    break;

                case BinaryKV1 kv1:
                    WalkKeyValues(kv1.KeyValues?.Root, null, 0);
                    break;

                case NTRO ntro:
                    WalkKeyValues(ntro.Output, null, 0);
                    break;

                default:
                    break;
            }
        }

        private void WalkKeyValues(KVObject? node, string? key, int depth)
        {
            if (node == null || depth > MaxDepth || nodeBudget <= 0)
            {
                return;
            }

            nodeBudget--;

            if (node.IsCollection)
            {
                foreach (var (name, child) in node.Children)
                {
                    WalkKeyValues(child, name, depth + 1);
                }

                return;
            }

            if (node.IsArray)
            {
                foreach (var element in node.Values)
                {
                    WalkKeyValues(element, key, depth + 1);
                }

                return;
            }

            if (node.ValueType != KVValueType.String)
            {
                return;
            }

            AddDataName(node.ToString(CultureInfo.InvariantCulture), key);
        }

        private void AddDataName(string? value, string? key)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxNameLength)
            {
                return;
            }

            if (PanoramaUrl.TryResolveResourceName(value, out var panoramaName))
            {
                Add(panoramaName, ResourceReferenceKind.Data, key);
                return;
            }

            // Resource data is full of interned strings that are not file names, such as bone names
            if (ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(value.AsSpan())) == ResourceType.Unknown)
            {
                return;
            }

            Add(value, ResourceReferenceKind.Data, key);
        }

        private void Add(string? name, ResourceReferenceKind kind, string? source = null, ulong id = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            name = name.Trim();

            // Compilers write markers such as "!embedded_sequence_data!<path>" and "$key:<name>" where a name would go
            if (name[0] is '!' or '$')
            {
                return;
            }

            var key = NormalizeKey(name);

            if (key == selfKey)
            {
                return;
            }

            if (entryIndex.TryGetValue(key, out var index))
            {
                var existing = entries[index];
                existing.Kinds |= kind;
                existing.Source ??= source;

                if (existing.Id == 0)
                {
                    existing.Id = id;
                }

                return;
            }

            entryIndex[key] = entries.Count;
            entries.Add(new Entry
            {
                Name = key,
                Kinds = kind,
                Source = source,
                Id = id,
            });
        }

        /// <remarks>
        /// Names are stored inconsistently, the same file can be written with either slash and with or
        /// without the compiled suffix, so both the lookup key and the displayed name use this spelling.
        /// </remarks>
        private static string NormalizeKey(string name)
        {
            var key = name.Replace('\\', '/');

            if (key.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                key = key[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            return key;
        }
    }
}
