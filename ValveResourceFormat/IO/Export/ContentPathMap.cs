using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.IO;

/// <summary>
/// Maps the content paths of files an export writes somewhere else to the paths they are written at, so references
/// to them can be rewritten.
/// </summary>
/// <remarks>
/// A value is mapped only when all of it is a mapped path, never a part of it. Matching ignores case, treats <c>\</c>
/// and <c>/</c> alike, ignores a leading <c>/</c> and a trailing <c>_c</c>, and a mapped value keeps the <c>_c</c> of
/// the value. A file entry wins over a folder entry, and the longest folder entry wins over the others. Reading is
/// thread-safe once every entry is added.
/// </remarks>
public sealed class ContentPathMap
{
    private readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> Folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether the map has no entries.
    /// </summary>
    public bool IsEmpty => Files.Count == 0 && Folders.Count == 0;

    /// <summary>
    /// Maps a file, such as <c>models/heroes/haze/haze.vmdl</c> to <c>models/heroes/haze_orig/haze_orig.vmdl</c>.
    /// </summary>
    /// <param name="from">The content path of the file.</param>
    /// <param name="to">The content path it is written at.</param>
    public void Add(string from, string to)
    {
        Files[Normalize(from)] = Normalize(to);
    }

    /// <summary>
    /// Maps a folder and every path below it, such as <c>maps/de_dust2</c> to <c>maps/de_dust2_orig</c>.
    /// </summary>
    /// <param name="fromFolder">The content path of the folder.</param>
    /// <param name="toFolder">The content path it is written at.</param>
    public void AddFolder(string fromFolder, string toFolder)
    {
        Folders[Normalize(fromFolder).TrimEnd('/')] = Normalize(toFolder).TrimEnd('/');
    }

    /// <summary>
    /// Maps a value that is a mapped path, or a path below a mapped folder.
    /// </summary>
    /// <param name="value">The value, such as a reference.</param>
    /// <param name="mapped">The path the value is written at, with forward slashes.</param>
    /// <returns>Whether the value is mapped.</returns>
    public bool TryMap(string value, [NotNullWhen(true)] out string? mapped)
    {
        mapped = null;

        if (IsEmpty || value.Length == 0)
        {
            return false;
        }

        var path = Normalize(value);
        var compiledSuffix = ContentPaths.HasCompiledSuffix(path) ? path[^GameFileLoader.CompiledFileSuffix.Length..] : string.Empty;
        path = path[..^compiledSuffix.Length];

        if (Files.TryGetValue(path, out var file))
        {
            mapped = file + compiledSuffix;
            return true;
        }

        var matchLength = -1;

        foreach (var (from, to) in Folders)
        {
            if (from.Length > matchLength && path.StartsWith(from, StringComparison.OrdinalIgnoreCase)
                && (path.Length == from.Length || path[from.Length] == '/'))
            {
                matchLength = from.Length;
                mapped = to + path[from.Length..] + compiledSuffix;
            }
        }

        return mapped != null;
    }

    /// <summary>
    /// Maps a value that is a mapped path, see <see cref="TryMap"/>.
    /// </summary>
    /// <returns>The path the value is written at, or the value itself when it is not mapped.</returns>
    public string Map(string value) => TryMap(value, out var mapped) ? mapped : value;

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}

/// <summary>
/// Rewrites references with a <see cref="ContentPathMap"/> that may be null, which leaves them as they are.
/// </summary>
internal static class ContentPathMapExtensions
{
    /// <inheritdoc cref="ContentPathMap.Map"/>
    public static string MapPath(this ContentPathMap? map, string value) => map?.Map(value) ?? value;

    /// <summary>
    /// Maps a source file a resource was authored from. With no entries every source file keeps its path. With
    /// entries, a source file the map does not move gets none, and is written next to the moved resource instead.
    /// </summary>
    /// <returns>Whether the source file has a path.</returns>
    public static bool TryMapSourceFile(this ContentPathMap? map, string sourceFile, [NotNullWhen(true)] out string? mapped)
    {
        if (map is not { IsEmpty: false })
        {
            mapped = sourceFile;
            return true;
        }

        return map.TryMap(sourceFile, out mapped);
    }

    /// <summary>
    /// Maps every string in a KeyValues tree that is a mapped path, see <see cref="ContentPathMap.TryMap"/>. The tree is not changed.
    /// </summary>
    /// <returns>The tree itself when nothing is mapped, otherwise a copy of it with the mapped strings.</returns>
    public static KVObject MapReferences(this ContentPathMap? map, KVObject value)
    {
        if (map == null)
        {
            return value;
        }

        switch (value.ValueType)
        {
            case KVValueType.String:
                return map.TryMap((string)value, out var mapped) ? new KVObject(mapped) { Flag = value.Flag } : value;

            case KVValueType.Collection:
            {
                List<KeyValuePair<string, KVObject>>? children = null;
                var index = 0;

                foreach (var (key, child) in value.Children)
                {
                    var mappedChild = map.MapReferences(child);

                    if (children == null && !ReferenceEquals(mappedChild, child))
                    {
                        children = [.. value.Children.Take(index)];
                    }

                    children?.Add(new(key, mappedChild));
                    index++;
                }

                return children == null ? value : WithFlag(KVObject.ListCollection(children), value.Flag);
            }

            case KVValueType.Array when !value.IsPackedArray:
            {
                var elements = value.Elements;
                List<KVObject>? mappedElements = null;

                for (var i = 0; i < elements.Count; i++)
                {
                    var mappedElement = map.MapReferences(elements[i]);

                    if (mappedElements == null && !ReferenceEquals(mappedElement, elements[i]))
                    {
                        mappedElements = [.. elements.Take(i)];
                    }

                    mappedElements?.Add(mappedElement);
                }

                return mappedElements == null ? value : WithFlag(KVObject.Array(mappedElements), value.Flag);
            }

            default:
                return value;
        }
    }

    /// <summary>
    /// Maps every string and string array item in the attributes of a datamodel that is a mapped path, see
    /// <see cref="ContentPathMap.TryMap"/>. The datamodel is changed in place.
    /// </summary>
    public static void MapReferences(this ContentPathMap? map, Datamodel.Datamodel datamodel)
    {
        if (map == null)
        {
            return;
        }

        var visited = new HashSet<Datamodel.Element>(ReferenceEqualityComparer.Instance);

        MapAttributes(map, datamodel.PrefixAttributes, visited);

        if (datamodel.Root is { } root)
        {
            MapAttributes(map, root, visited);
        }
    }

    private static void MapAttributes(ContentPathMap map, Datamodel.AttributeList attributes, HashSet<Datamodel.Element> visited)
    {
        if (attributes is Datamodel.Element element && !visited.Add(element))
        {
            return;
        }

        foreach (var (key, value) in attributes.GetAllAttributesForSerialization().ToList())
        {
            switch (value)
            {
                case string text when map.TryMap(text, out var mapped):
                    attributes[key] = mapped;
                    break;

                case Datamodel.Element child:
                    MapAttributes(map, child, visited);
                    break;

                case IList<string> texts:
                    for (var i = 0; i < texts.Count; i++)
                    {
                        if (map.TryMap(texts[i], out var mappedText))
                        {
                            texts[i] = mappedText;
                        }
                    }

                    break;

                case IEnumerable<Datamodel.Element?> children:
                    foreach (var arrayChild in children)
                    {
                        if (arrayChild != null)
                        {
                            MapAttributes(map, arrayChild, visited);
                        }
                    }

                    break;
            }
        }
    }

    private static KVObject WithFlag(KVObject value, KVFlag flag)
    {
        value.Flag = flag;
        return value;
    }
}
