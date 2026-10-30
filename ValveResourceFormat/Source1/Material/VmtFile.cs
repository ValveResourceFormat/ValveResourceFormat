using System.IO;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Reads a Source 1 material, resolving patch, #base, #include and conditions for a Windows DX90 target.
/// </summary>
internal sealed partial class VmtFile
{
    private static readonly string[] ShaderSuffixes =
        ["_dx9_hdr", "_dx90", "_dx95", "_dx9", "_dx80", "_dx81", "_dx8", "_dx70", "_dx7", "_dx60", "_dx6"];

    public string ShaderName => Root.Name;
    public string NormalizedShaderName => NormalizeShaderName(Root.Name);
    public IReadOnlyDictionary<string, string> Parameters { get; }
    public VmtParameters Values { get; }
    public VmtNode Root => Roots[0];
    public IReadOnlyList<VmtNode> Roots { get; }
    public IReadOnlyList<VmtDiagnostic> Diagnostics { get; }

    private VmtFile(string path, IReadOnlyList<VmtNode> roots, List<VmtDiagnostic> diagnostics, bool hdr)
    {
        Roots = roots;
        Diagnostics = diagnostics.AsReadOnly();
        Parameters = SelectParameters(Root, hdr);
        Values = new VmtParameters(Parameters, path);
    }

    /// <summary>
    /// Loads a material. The file loader receives normalized paths such as <c>materials/foo.vmt</c> and returns null when the file is missing.
    /// The optional resolver receives condition symbols without '$'; returning null uses the built-in DX90 PC defaults.
    /// </summary>
    public static VmtFile Load(string path, Func<string, Stream?> openFile,
        Func<string, bool?>? resolveCondition = null, Action<VmtDiagnostic>? reportDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(openFile);

        var context = new LoadContext(openFile, resolveCondition, reportDiagnostic);
        var normalizedPath = NormalizePath(path, ".vmt");
        var roots = LoadRoots(normalizedPath, context);

        if (roots.Count == 0)
        {
            throw new InvalidDataException($"VMT has no active shader root: {path}");
        }

        var hdr = context.Resolve("hdr") == true;

        return new VmtFile(normalizedPath, roots, context.Diagnostics, hdr);
    }

    public static string NormalizeShaderName(string shader)
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

    private static Dictionary<string, string> SelectParameters(VmtNode root, bool hdr)
    {
        var values = new Dictionary<string, string>(root.Values, StringComparer.OrdinalIgnoreCase);

        if (FindFallbackBlock(root, hdr) is not { } block)
        {
            return values;
        }

        foreach (var pair in block.Values)
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

    private static List<VmtNode> LoadRoots(string path, LoadContext context)
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

            using var stream = context.OpenFile(path) ?? throw new FileNotFoundException($"VMT not found: {path}", path);
            using var reader = new StreamReader(stream);
            var parser = new Parser(reader.ReadToEnd(), path, context);
            var roots = parser.ReadDocument();

            // Native KeyValues appends #include roots after local roots, regardless of directive position.
            foreach (var include in parser.Includes)
            {
                roots.AddRange(LoadRoots(ResolveDirectivePath(path, include), context));
            }

            foreach (var basePath in parser.Bases)
            {
                var bases = LoadRoots(ResolveDirectivePath(path, basePath), context);

                if (roots.Count != 0 && bases.Count != 0)
                {
                    roots[0] = Merge(roots[0], bases[0], replace: false);
                }
            }

            if (roots.Count != 0 && roots[0].Name.Equals("patch", StringComparison.OrdinalIgnoreCase))
            {
                var root = roots[0];

                if (!root.Values.TryGetValue("include", out var include) || string.IsNullOrWhiteSpace(include))
                {
                    throw new InvalidDataException($"VMT patch has no include: {path}");
                }

                var source = LoadRoots(NormalizePath(include, ".vmt"), context);

                if (source.Count == 0)
                {
                    throw new InvalidDataException($"VMT patch include has no active shader root: {path}");
                }

                var merged = source[0];

                if (root.GetBlock("insert") is { } insert)
                {
                    merged = Insert(merged, insert);
                }

                if (root.GetBlock("replace") is { } replace)
                {
                    merged = Merge(merged, replace, replace: true);
                }

                roots[0] = merged;
                roots.InsertRange(1, source.GetRange(1, source.Count - 1));
            }

            context.Cache.Add(path, roots);
            return roots;
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

    // A patch insert sets its values over the ones of the material and ignores its blocks
    private static VmtNode Insert(VmtNode target, VmtNode changes)
    {
        var children = new List<VmtNode>(target.Children);

        foreach (var change in changes.Children)
        {
            if (change.IsBlock)
            {
                continue;
            }

            var index = children.FindLastIndex(child => !child.IsBlock && child.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                children.Add(change);
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
                    throw new InvalidDataException($"VMT resource path traverses its mount: {path}");
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

        if (extension.Equals(".vtf", StringComparison.OrdinalIgnoreCase))
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^extension.Length];
            }

            path += extension;
        }
        else if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            path += extension;
        }

        return path;
    }

    private sealed class LoadContext(Func<string, Stream?> openFile, Func<string, bool?>? resolveCondition,
        Action<VmtDiagnostic>? reportDiagnostic)
    {
        public Func<string, Stream?> OpenFile { get; } = openFile;
        public HashSet<string> Active { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<VmtNode>> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<VmtDiagnostic> Diagnostics { get; } = [];
        public int FileCount;

        public void Report(string path, int line, VmtDiagnosticKind kind, string message)
        {
            var diagnostic = new VmtDiagnostic(path, line, kind, message);
            Diagnostics.Add(diagnostic);
            reportDiagnostic?.Invoke(diagnostic);
        }

        public bool? Resolve(string symbol)
        {
            var resolved = resolveCondition?.Invoke(symbol);

            if (resolved is not null)
            {
                return resolved;
            }

            return symbol.ToUpperInvariant() switch
            {
                "WIN32" or "WINDOWS" or "PC" or "DX90" or "DX90_20B" or "HDR" or "SRGB" or "SRGB_PC" => true,
                "WIN64" => Environment.Is64BitProcess,
                "LDR" => Resolve("hdr") != true,
                "360" or "X360" or "XBOX" or "XBOX360" or "PS3" or "SONYPS3" or "GAMECONSOLE" or "OSX" or "LINUX" or "POSIX"
                    or "WINXP" or "DECK" or "DX60" or "DX70" or "DX80" or "DX81" or "DX95" => false,
                _ => null
            };
        }
    }
}
