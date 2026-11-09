using System.Globalization;
using System.IO;
using ValveKeyValue;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Reads a Source 1 material, resolving patch, #base, #include and conditions for a Windows DX90 target.
/// </summary>
internal sealed partial class VmtFile
{
    private static readonly string[] ShaderSuffixes =
        ["_dx9_hdr", "_dx90", "_dx95", "_dx9", "_dx80", "_dx81", "_dx8", "_dx70", "_dx7", "_dx60", "_dx6"];

    // The symbols that can hold in a bracket condition; the others are always false
    private static readonly string[] ConditionSymbols =
        ["WIN32", "WINDOWS", "HDR", "LDR", "SRGB", "SRGB_PC", "GPU>=1", "GPU>=2", "GPU>=3"];

    public string MaterialPath { get; }
    public string ShaderName => Root.Name;
    public string NormalizedShaderName => NormalizeShaderName(Root.Name);
    public IReadOnlyDictionary<string, string> Parameters { get; }
    public VmtParameters Values { get; }
    public VmtNode Root { get; }

    /// <summary>
    /// The proxies of the <c>Proxies</c> block in file order, up to <see cref="MaxProxies"/>.
    /// </summary>
    public IReadOnlyList<VmtProxy> Proxies { get; }

    public const int MaxProxies = 256;

    private VmtFile(string path, VmtNode root, bool hdr)
    {
        MaterialPath = path;
        Root = root;
        var fallbackBlock = FindFallbackBlock(Root, hdr);
        Parameters = SelectParameters(Root, fallbackBlock);
        Values = new VmtParameters(Parameters, path);
        Proxies = ReadProxies(Root, fallbackBlock);
    }

    // A selected fallback block with its own Proxies replaces the ones of the root
    private static List<VmtProxy> ReadProxies(VmtNode root, VmtNode? fallbackBlock)
    {
        var proxies = new List<VmtProxy>();
        var block = fallbackBlock?.GetBlock("Proxies") ?? root.GetBlock("Proxies");

        if (block is null)
        {
            return proxies;
        }

        foreach (var child in block.Children)
        {
            if (!child.IsBlock)
            {
                continue;
            }

            if (proxies.Count == MaxProxies)
            {
                break;
            }

            proxies.Add(new VmtProxy(child.Name, child.Values));
        }

        return proxies;
    }

    /// <summary>
    /// Loads a material. The file loader receives normalized paths such as <c>materials/foo.vmt</c> and returns null when the file is missing.
    /// The optional resolver receives condition symbols without '$'; returning null uses the built-in DX90 PC defaults.
    /// </summary>
    public static VmtFile Load(string path, Func<string, Stream?> openFile, Func<string, bool?>? resolveCondition = null)
    {
        ArgumentNullException.ThrowIfNull(openFile);

        var context = new LoadContext(openFile, resolveCondition);
        var normalizedPath = NormalizePath(path, ".vmt");
        var root = LoadRoot(normalizedPath, context);
        var hdr = context.Resolve("hdr");

        return new VmtFile(normalizedPath, root, hdr);
    }

    private static string NormalizeShaderName(string shader)
    {
        shader = shader.ToLowerInvariant();

        foreach (var suffix in ShaderSuffixes)
        {
            if (shader.EndsWith(suffix, StringComparison.Ordinal))
            {
                return shader[..^suffix.Length];
            }
        }

        return shader;
    }

    private static Dictionary<string, string> SelectParameters(VmtNode root, VmtNode? fallbackBlock)
    {
        var values = new Dictionary<string, string>(root.Values, StringComparer.OrdinalIgnoreCase);

        if (fallbackBlock is null)
        {
            return values;
        }

        foreach (var pair in fallbackBlock.Values)
        {
            values[pair.Key] = pair.Value;
        }

        return values;
    }

    // Only the first matching block applies. A block is named by its condition, or by the shader and the condition.
    private static VmtNode? FindFallbackBlock(VmtNode root, bool hdr)
    {
        foreach (var condition in GetFallbackConditions(hdr))
        {
            var block = root.GetBlock(condition) ?? root.GetBlock($"{root.Name}_{condition}");

            if (block is not null)
            {
                return block;
            }
        }

        return null;
    }

    // The conditions that hold on a PC with DirectX 9.0c (support level 95) and the highest GPU level, in priority order
    private static IEnumerable<string> GetFallbackConditions(bool hdr)
    {
        yield return "GPU>=1";
        yield return "GPU>=2";
        yield return ">=DX90_20b";
        yield return ">=DX90";
        yield return ">DX90";

        if (hdr)
        {
            yield return "hdr_dx9";
            yield return "hdr";
        }
        else
        {
            yield return "ldr";
        }

        yield return "srgb";
        yield return "dx9";
    }

    private static VmtNode LoadRoot(string path, LoadContext context)
    {
        if (context.Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (context.Active.Count >= 32 || !context.Active.Add(path))
        {
            throw new InvalidDataException($"Cyclic or excessively nested VMT include: {path}");
        }

        try
        {
            if (++context.FileCount > 256)
            {
                throw new InvalidDataException("VMT dependency count exceeds 256 files.");
            }

            var root = ReadRoot(path, context);

            if (IsPatch(root))
            {
                root = ExpandPatch(path, root, context);
            }

            context.Cache.Add(path, root);
            return root;
        }
        finally
        {
            context.Active.Remove(path);
        }
    }

    private static VmtNode Merge(VmtNode target, VmtNode changes, bool replace)
    {
        var children = new List<VmtNode>(target.Children);

        foreach (var change in changes.Children)
        {
            var index = change.IsBlock
                ? children.FindIndex(child => child.IsBlock && child.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase))
                : children.FindLastIndex(child => !child.IsBlock && child.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                if (!replace)
                {
                    children.Add(change);
                }

                continue;
            }

            if (children[index].IsBlock && change.IsBlock)
            {
                children[index] = Merge(children[index], change, replace);
            }
            else if (replace)
            {
                children[index] = change;
            }
        }

        return new VmtNode(target.Name, target.Value, children);
    }

    private static VmtNode ReadRoot(string path, LoadContext context)
    {
        if (++context.FileCount > 256)
        {
            throw new InvalidDataException("VMT dependency count exceeds 256 files.");
        }

        using var stream = context.OpenFile(path) ?? throw new FileNotFoundException($"VMT not found: {path}", path);
        var directives = new DirectiveRecorder();
        var options = new KVSerializerOptions
        {
            HasEscapeSequences = false,
            FileLoader = directives,
        };

        foreach (var symbol in ConditionSymbols)
        {
            if (context.Resolve(symbol))
            {
                options.Conditions.Add(symbol);
            }
        }

        KVDocument document;

        try
        {
            document = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, options);
        }
        catch (KeyValueException e)
        {
            throw new InvalidDataException($"Cannot read VMT {path}: {e.Message}", e);
        }

        if (!document.Root.IsCollection)
        {
            throw new InvalidDataException($"VMT has no shader block: {path}");
        }

        var root = ConvertBlock(document.Name ?? string.Empty, document.Root, context, 0);

        foreach (var basePath in directives.Paths)
        {
            var baseRoot = LoadRoot(ResolveDirectivePath(path, basePath), context);
            root = Merge(root, baseRoot, replace: false);
        }

        return root;
    }

    private static VmtNode ConvertBlock(string name, KVObject block, LoadContext context, int depth)
    {
        if (depth >= 64)
        {
            throw new InvalidDataException("VMT block nesting limit exceeded.");
        }

        var children = new List<VmtNode>();

        foreach (var (key, value) in block.Children)
        {
            var childName = ResolveKeyCondition(key, context);

            if (childName is null)
            {
                continue;
            }

            if (value.IsCollection)
            {
                children.Add(ConvertBlock(childName, value, context, depth + 1));
            }
            else
            {
                children.Add(new VmtNode(childName, value.ToString(CultureInfo.InvariantCulture), []));
            }
        }

        return new VmtNode(name, null, children);
    }

    // A key can start with a condition and a question mark, like "!srgb?$key". Returns the key without the
    // condition, or null when the condition does not hold.
    private static string? ResolveKeyCondition(string key, LoadContext context)
    {
        var separator = key.IndexOf('?', StringComparison.Ordinal);

        if (separator < 0)
        {
            return key;
        }

        var name = key[(separator + 1)..];
        var symbol = key[..separator];
        var negate = false;

        while (symbol.StartsWith('!'))
        {
            negate = !negate;
            symbol = symbol[1..];
        }

        if (symbol.StartsWith('$'))
        {
            symbol = symbol[1..];
        }

        if (symbol.Length == 0 || name.Length == 0)
        {
            return null;
        }

        var value = context.Resolve(symbol);
        var holds = negate ? !value : value;

        return holds ? name : null;
    }

    private static bool IsPatch(VmtNode root)
        => root.Name.Equals("patch", StringComparison.OrdinalIgnoreCase);

    // The chain of patch files is followed first and their insert and replace sections are gathered, so the
    // values of the patch nearest the shader win. The sections are applied to the final material once, insert
    // before replace.
    private static VmtNode ExpandPatch(string path, VmtNode root, LoadContext context)
    {
        const int MaxPatchDepth = 10;

        var insert = new VmtNode("insert", null, []);
        var replace = new VmtNode("replace", null, []);
        var current = root;

        for (var depth = 0; depth < MaxPatchDepth && IsPatch(current); depth++)
        {
            var patch = current;

            if (patch.GetBlock("insert") is { } insertSection)
            {
                insert = Overlay(insert, insertSection);
            }

            if (patch.GetBlock("replace") is { } replaceSection)
            {
                replace = Overlay(replace, replaceSection);
            }

            if (!patch.Values.TryGetValue("include", out var include) || string.IsNullOrWhiteSpace(include))
            {
                throw new InvalidDataException($"VMT patch has no include: {path}");
            }

            current = ReadRoot(NormalizePath(include, ".vmt"), context);
        }

        if (IsPatch(current))
        {
            throw new InvalidDataException($"VMT patch include has no active shader root: {path}");
        }

        var merged = Overlay(current, insert);

        return Merge(merged, replace, replace: true);
    }

    /// <summary>A patch that only sets the cube map of the material it includes.</summary>
    /// <param name="Target">The path of the included material.</param>
    /// <param name="Envmap">The cube map the patch sets, or <see langword="null"/> when it names none.</param>
    internal readonly record struct EnvmapPatch(string Target, string? Envmap);

    /// <summary>
    /// Gets the material a patch stands for when the only thing it changes is <c>$envmap</c>. Source 2 has no cube map per material,
    /// so such a patch is the material it includes.
    /// </summary>
    /// <param name="path">The path of the material file, such as <c>materials/maps/mymap/wall_0_0_64.vmt</c>.</param>
    /// <param name="openFile">Opens a file by normalized path, or returns null when it is missing.</param>
    /// <returns>The included material and the cube map, or <see langword="null"/> when the file is missing or is not such a patch.</returns>
    internal static EnvmapPatch? GetEnvmapPatch(string path, Func<string, Stream?> openFile)
    {
        try
        {
            var context = new LoadContext(openFile, null);
            var root = ReadRoot(NormalizePath(path, ".vmt"), context);

            if (!IsPatch(root))
            {
                return null;
            }

            string? include = null;
            string? envmap = null;

            foreach (var child in root.Children)
            {
                if (!child.IsBlock && child.Name.Equals("include", StringComparison.OrdinalIgnoreCase))
                {
                    include = child.Value;
                }
                else if (!child.IsBlock || !SetsOnlyEnvmap(child, ref envmap))
                {
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(include))
            {
                return null;
            }

            return new EnvmapPatch(include, envmap);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private static bool SetsOnlyEnvmap(VmtNode section, ref string? envmap)
    {
        var isSection = section.Name.Equals("insert", StringComparison.OrdinalIgnoreCase)
            || section.Name.Equals("replace", StringComparison.OrdinalIgnoreCase);

        if (!isSection)
        {
            return false;
        }

        return HasOnlyEnvmap(section, ref envmap);
    }

    // Some patches set the cube map inside a block named for the shader, and water patches also empty the proxies
    private static bool HasOnlyEnvmap(VmtNode block, ref string? envmap)
    {
        foreach (var child in block.Children)
        {
            if (!child.IsBlock)
            {
                if (!child.Name.Equals("$envmap", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                envmap = child.Value;
                continue;
            }

            if (child.Name.Equals("Proxies", StringComparison.OrdinalIgnoreCase))
            {
                if (child.Children.Count > 0)
                {
                    return false;
                }
            }
            else if (!HasOnlyEnvmap(child, ref envmap))
            {
                return false;
            }
        }

        return true;
    }

    // Sets the values of changes over the ones of the target and merges its blocks into the blocks of the same name
    private static VmtNode Overlay(VmtNode target, VmtNode changes)
    {
        var children = new List<VmtNode>(target.Children);

        foreach (var change in changes.Children)
        {
            var index = change.IsBlock
                ? children.FindIndex(child => child.IsBlock && child.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase))
                : children.FindLastIndex(child => !child.IsBlock && child.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                children.Add(change);
            }
            else if (change.IsBlock)
            {
                children[index] = Overlay(children[index], change);
            }
            else
            {
                children[index] = change;
            }
        }

        return new VmtNode(target.Name, target.Value, children);
    }

    private static string ResolveDirectivePath(string source, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidDataException("Empty VMT directive path.");
        }

        reference = reference.Replace('\\', '/');

        if (reference.StartsWith('/') || reference.Contains(':', StringComparison.Ordinal) || reference.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"VMT directive path must be relative: {reference}");
        }

        var path = source[..(source.LastIndexOf('/') + 1)] + reference;
        var segments = new List<string>();

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count <= 1)
                {
                    throw new InvalidDataException($"VMT directive escapes the materials directory: {reference}");
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        if (segments.Count < 2)
        {
            throw new InvalidDataException($"VMT directive does not name a file: {reference}");
        }

        return string.Join('/', segments).ToLowerInvariant();
    }

    public static string NormalizePath(string path, string extension)
        => TryNormalizePath(path, extension) ?? throw new InvalidDataException($"VMT resource path traverses its mount: {path}");

    /// <summary>
    /// Normalizes a path like <see cref="NormalizePath"/>, but returns <see langword="null"/> for a path that climbs
    /// out of its mount, which the game never finds.
    /// </summary>
    public static string? TryNormalizePath(string path, string extension)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Empty VMT resource path.");
        }

        path = path.Trim().Replace('\\', '/').TrimStart('/');

        if (path.Contains(':', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"VMT resource path must be mount-relative: {path}");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (normalized.Count == 0 || (normalized.Count == 1 && normalized[0].Equals("materials", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                normalized.RemoveAt(normalized.Count - 1);
            }
            else if (segment != ".")
            {
                normalized.Add(segment);
            }
        }

        if (normalized.Count == 1 && normalized[0].Equals("materials", StringComparison.OrdinalIgnoreCase) && path.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Empty VMT resource path.");
        }

        path = string.Join('/', normalized).ToLowerInvariant();

        if (!path.StartsWith("materials/", StringComparison.Ordinal))
        {
            path = "materials/" + path;
        }

        if (path == "materials/")
        {
            throw new InvalidDataException("Empty VMT resource path.");
        }

        if (extension.Equals(Source1Paths.TextureExtension, StringComparison.OrdinalIgnoreCase))
        {
            path = Source1Paths.StripTextureExtension(path) + extension;
        }
        else if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            path += extension;
        }

        return path;
    }

    // Remembers the files named by #base and #include directives instead of reading them, so they go through the same
    // loading and merging as patch includes.
    private sealed class DirectiveRecorder : IIncludedFileLoader
    {
        public List<string> Paths { get; } = [];

        public Stream OpenFile(string filePath)
        {
            Paths.Add(filePath);
            return new MemoryStream("\"\" {}"u8.ToArray());
        }
    }

    private sealed class LoadContext(Func<string, Stream?> openFile, Func<string, bool?>? resolveCondition)
    {
        public Func<string, Stream?> OpenFile { get; } = openFile;
        public HashSet<string> Active { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VmtNode> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int FileCount;

        // Unknown symbols are false.
        public bool Resolve(string symbol)
        {
            var resolved = resolveCondition?.Invoke(symbol);

            if (resolved is not null)
            {
                return resolved.Value;
            }

            return symbol.ToUpperInvariant() switch
            {
                "GPU>=1" or "GPU>=2" or "GPU>=3" or "WIN32" or "WINDOWS" or "HDR" or "SRGB" or "SRGB_PC" => true,
                "LDR" => !Resolve("hdr"),
                _ => false
            };
        }
    }
}
