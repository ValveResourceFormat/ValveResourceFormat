using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ValveResourceFormat.ResourceTypes;

/// <summary>
/// Resolves the <c>file://</c> and <c>s2r://</c> urls panorama files use into compiled resource names.
/// </summary>
public static class PanoramaUrl
{
    private const string Scheme = "file://";
    private const string ResourceScheme = "s2r://";
    private const string ImagesRoot = "{images}";
    private const string ResourcesRoot = "{resources}";

    /// <summary>
    /// Turns a panorama url such as <c>file://{images}/spellicons/marci_unleash.png</c> into the resource
    /// name it compiles to, such as <c>panorama/images/spellicons/marci_unleash_png.vtex</c>. An <c>s2r://</c> url
    /// names the file by its full path instead.
    /// </summary>
    /// <param name="value">The url as it is stored in the resource.</param>
    /// <param name="resourceName">The resource name the url points at.</param>
    /// <returns><c>true</c> when the url could be resolved.</returns>
    public static bool TryResolveResourceName(string value, [NotNullWhen(true)] out string? resourceName)
    {
        resourceName = null;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.StartsWith(ResourceScheme, StringComparison.OrdinalIgnoreCase))
        {
            var name = value[ResourceScheme.Length..].Replace('\\', '/').TrimStart('/');

            if (Path.GetExtension(name.AsSpan()).IsEmpty)
            {
                return false;
            }

            resourceName = CompiledName(name);
            return true;
        }

        if (!value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = value[Scheme.Length..].Replace('\\', '/');
        string root;

        if (path.StartsWith(ImagesRoot, StringComparison.OrdinalIgnoreCase))
        {
            root = "panorama/images";
            path = path[ImagesRoot.Length..];
        }
        else if (path.StartsWith(ResourcesRoot, StringComparison.OrdinalIgnoreCase))
        {
            root = "panorama";
            path = path[ResourcesRoot.Length..];
        }
        else
        {
            return false;
        }

        path = path.TrimStart('/');

        if (Path.GetExtension(path.AsSpan()).IsEmpty)
        {
            return false;
        }

        resourceName = string.Concat(root, "/", CompiledName(path));
        return true;
    }

    private static string CompiledName(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());
        var withoutExtension = path[..^extension.Length];
        var compiled = extension[1..] switch
        {
            var e when e.Equals("xml", StringComparison.OrdinalIgnoreCase) => ".vxml",
            var e when e.Equals("css", StringComparison.OrdinalIgnoreCase) => ".vcss",
            var e when e.Equals("js", StringComparison.OrdinalIgnoreCase) => ".vjs",
            var e when e.Equals("ts", StringComparison.OrdinalIgnoreCase) => ".vts",
            var e when e.Equals("svg", StringComparison.OrdinalIgnoreCase) => ".vsvg",
            var e when e.Equals("webm", StringComparison.OrdinalIgnoreCase) => ".webm",
            _ => null,
        };

        if (compiled != null)
        {
            return string.Concat(withoutExtension, compiled);
        }

        // Images keep their source extension in the compiled name, as in "background_png.vtex"
        if (ResourceTypeExtensions.DetermineByFileExtension(extension) != ResourceType.Unknown)
        {
            return path;
        }

        return string.Concat(withoutExtension, "_", extension[1..], ".vtex");
    }
}
