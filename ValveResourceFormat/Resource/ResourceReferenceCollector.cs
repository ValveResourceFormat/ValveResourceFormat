using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
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

    /// <summary>
    /// Collects the references of a compiled resource straight from its bytes, reading its external reference
    /// list, its edit info and only the string tables of its binary KV3 blocks.
    /// </summary>
    /// <remarks>
    /// This finds the same names as reading the resource and calling <see cref="Collect(Resource)"/> at a fraction
    /// of the cost, but data names carry no member name. A resource with a block that is not binary KV3 is read in full.
    /// </remarks>
    /// <param name="input">A seekable stream over the resource.</param>
    /// <param name="fileName">The name of the resource, which is left out of its own references.</param>
    /// <returns>The references, in the order they were found.</returns>
    public static IReadOnlyList<ResourceReference> CollectFromStream(Stream input, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        using var resource = new Resource { FileName = fileName };
        var collector = new Collector(fileName);

        reader.BaseStream.Position = 4;

        var headerVersion = reader.ReadUInt16();

        if (headerVersion != Resource.KnownHeaderVersion)
        {
            throw new UnexpectedMagicException("Unexpected resource header", headerVersion, nameof(headerVersion));
        }

        reader.BaseStream.Position = 8;

        var blockOffset = reader.ReadUInt32();
        var blockCount = reader.ReadUInt32();
        var blocks = new List<(BlockType Type, uint Offset, uint Size)>((int)Math.Min(blockCount, 1024));

        reader.BaseStream.Position = 8 + blockOffset;

        for (var i = 0; i < blockCount; i++)
        {
            var type = (BlockType)reader.ReadUInt32();
            var position = (uint)reader.BaseStream.Position;
            var offset = position + reader.ReadUInt32();
            var size = reader.ReadUInt32();

            if (size > 0 && offset + size <= reader.BaseStream.Length)
            {
                blocks.Add((type, offset, size));
            }
        }

        foreach (var (type, offset, size) in blocks)
        {
            switch (type)
            {
                case BlockType.RERL:
                    var externalReferences = new ResourceExtRefList { Resource = resource, Offset = offset, Size = size };
                    externalReferences.Read(reader);
                    collector.CollectExternalReferences(externalReferences);
                    break;

                case BlockType.REDI:
                case BlockType.RED2:
                    ResourceEditInfo editInfo = type == BlockType.RED2
                        ? new ResourceEditInfo2 { Resource = resource, Offset = offset, Size = size }
                        : new ResourceEditInfo { Resource = resource, Offset = offset, Size = size };
                    editInfo.Read(reader);
                    collector.CollectEditInfo(editInfo);
                    break;

                default:
                    reader.BaseStream.Position = offset;

                    if (size < 4 || !BinaryKV3.IsBinaryKV3(reader.ReadUInt32()))
                    {
                        input.Position = 0;
                        resource.Read(input, leaveOpen: true);
                        return Collect(resource);
                    }

                    if (type == BlockType.FLCI)
                    {
                        var sourceLocations = new BinaryKV3(type) { Resource = resource, Offset = offset, Size = size };
                        sourceLocations.Read(reader);
                        collector.CollectBlock(sourceLocations);
                        break;
                    }

                    foreach (var value in BinaryKV3.ReadStringTable(reader, resource, offset, size))
                    {
                        collector.AddDataString(value, null);
                    }

                    break;
            }
        }

        return collector.Build();
    }

    /// <summary>
    /// Collects the file names among the strings of a data block, such as the string table
    /// <see cref="BinaryKV3.ReadStringTable"/> returns.
    /// </summary>
    /// <param name="values">The strings.</param>
    /// <returns>The references, all of kind <see cref="ResourceReferenceKind.Data"/>, in the order they were found.</returns>
    public static IReadOnlyList<ResourceReference> CollectDataNames(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var collector = new Collector(null);

        foreach (var value in values)
        {
            collector.AddDataString(value, null);
        }

        return collector.Build();
    }

    private sealed class Collector(string? selfFileName)
    {
        private const int MaxDepth = 64;
        private const int MaxNodes = 1_000_000;
        private const int MaxNameLength = 512;

        private static readonly SearchValues<char> TextSeparators = SearchValues.Create(" \t\r\n\"'`(),;=[]<>|+");

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

                    CollectText(Encoding.UTF8.GetString(panorama.Data), null);
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

                case SoundStackScript soundStackScript:
                    foreach (var (name, script) in soundStackScript.SoundStackScriptValue)
                    {
                        CollectText(script, name);
                    }

                    break;

                case ResponseRules responseRules:
                    foreach (var include in responseRules.Includes)
                    {
                        Add(include.Name, ResourceReferenceKind.Data, "include");
                    }

                    break;

                case EntityLump entityLump:
                    CollectEntityLump(entityLump);
                    break;

                case World world:
                    WalkKeyValues(world.Data, null, 0);

                    foreach (var worldNode in world.GetWorldNodeNames())
                    {
                        Add(string.Concat(worldNode, ".vwnod"), ResourceReferenceKind.Data, "m_worldNodePrefix");
                    }

                    break;

                case KeyValuesOrNTRO keyValues:
                    WalkKeyValues(keyValues.Data, null, 0);
                    break;

                case BinaryKV3 { Type: BlockType.FLCI } sourceLocations:
                    CollectSourceFiles(sourceLocations.Data?.Root);
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

        /// <remarks>
        /// Entity key values can be stored as a binary blob per entity, which only the lump can decode.
        /// </remarks>
        private void CollectEntityLump(EntityLump entityLump)
        {
            const string EntityKeyValues = "m_entityKeyValues";

            foreach (var (name, child) in entityLump.Data.Children)
            {
                if (name != EntityKeyValues)
                {
                    WalkKeyValues(child, name, 1);
                }
            }

            List<EntityLump.Entity> entities;

            try
            {
                entities = entityLump.GetEntities();
            }
            catch (Exception e) when (e is UnexpectedMagicException or InvalidDataException or EndOfStreamException)
            {
                WalkKeyValues(entityLump.Data[EntityKeyValues], EntityKeyValues, 1);
                return;
            }

            foreach (var entity in entities)
            {
                foreach (var (name, value) in entity.Children)
                {
                    WalkKeyValues(value, name, 1);
                }

                foreach (var connection in entity.Connections ?? [])
                {
                    AddDataName(connection.OverrideParam, "m_overrideParam");
                }
            }
        }

        private void CollectSourceFiles(KVObject? root)
        {
            if (root?["flc_file_list"] is not { IsArray: true } fileList)
            {
                return;
            }

            foreach (var file in fileList.Values)
            {
                if (file.ValueType == KVValueType.String)
                {
                    Add(ContentRelativeName(file.ToString(CultureInfo.InvariantCulture)), ResourceReferenceKind.InputDependency);
                }
            }
        }

        /// <remarks>
        /// The block names its sources as "content/&lt;mod&gt;/&lt;path&gt;", at times below further build folders,
        /// while the edit info names them relative to the mod folder.
        /// </remarks>
        private static string ContentRelativeName(string name)
        {
            const string ContentFolder = "content/";

            var path = name.Replace('\\', '/');
            var start = path.Length;

            do
            {
                start = start == 0 ? -1 : path.LastIndexOf(ContentFolder, start - 1, StringComparison.OrdinalIgnoreCase);
            }
            while (start > 0 && path[start - 1] != '/');

            if (start < 0)
            {
                return name;
            }

            var modEnd = path.IndexOf('/', start + ContentFolder.Length);

            return modEnd < 0 ? name : path[(modEnd + 1)..];
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

            AddDataString(node.ToString(CultureInfo.InvariantCulture), key);
        }

        public void AddDataString(string value, string? key)
        {
            if (!AddDataName(value, key) && value.AsSpan().ContainsAny(TextSeparators))
            {
                CollectText(value, key);
            }
        }

        /// <summary>
        /// Adds the file names written in source text, such as the urls of a panorama layout or the names a script passes around.
        /// </summary>
        private void CollectText(ReadOnlySpan<char> text, string? key)
        {
            while (!text.IsEmpty)
            {
                var end = text.IndexOfAny(TextSeparators);
                var token = end < 0 ? text : text[..end];
                text = end < 0 ? [] : text[(end + 1)..];

                if (token.Length <= MaxNameLength && token.ContainsAny('/', '\\'))
                {
                    AddDataName(token.ToString(), key);
                }
            }
        }

        private bool AddDataName(string? value, string? key)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxNameLength)
            {
                return false;
            }

            // Format patterns such as "%s", sentences and bare names such as material layer labels are not file names
            if (value.Contains('%', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
                || !value.AsSpan().ContainsAny('/', '\\'))
            {
                return false;
            }

            if (PanoramaUrl.TryResolveResourceName(value, out var panoramaName))
            {
                Add(panoramaName, ResourceReferenceKind.Data, key);
                return true;
            }

            var extension = Path.GetExtension(value.AsSpan());

            // Sound events can name a sound by its source file, which compiles to a .vsnd
            if ((extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                && value.AsSpan().ContainsAny('/', '\\') && !value.AsSpan().ContainsAny(TextSeparators))
            {
                Add(string.Concat(value.AsSpan(0, value.Length - extension.Length), ".vsnd"), ResourceReferenceKind.Data, key);
                return true;
            }

            // Resource data is full of interned strings that are not file names, such as bone names
            if (ResourceTypeExtensions.DetermineByFileExtension(extension) == ResourceType.Unknown)
            {
                return false;
            }

            Add(value, ResourceReferenceKind.Data, key);
            return true;
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
