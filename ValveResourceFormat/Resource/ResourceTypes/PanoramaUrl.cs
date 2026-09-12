using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ValveResourceFormat.ResourceTypes;

/// <summary>
/// Resolves the <c>file://</c> urls panorama files use into compiled resource names.
/// </summary>
public static class PanoramaUrl
{
    private const string Scheme = "file://";
    private const string ImagesRoot = "{images}";
    private const string ResourcesRoot = "{resources}";

    /// <summary>
    /// Turns a panorama url such as <c>file://{images}/spellicons/marci_unleash.png</c> into the resource
    /// name it compiles to, such as <c>panorama/images/spellicons/marci_unleash_png.vtex</c>.
    /// </summary>
    /// <param name="value">The url as it is stored in the resource.</param>
    /// <param name="resourceName">The resource name the url points at.</param>
    /// <returns><c>true</c> when the url could be resolved.</returns>
    public static bool TryResolveResourceName(string value, [NotNullWhen(true)] out string? resourceName)
    {
        resourceName = null;

        if (string.IsNullOrEmpty(value) || !value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
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

        if (path.Length == 0)
        {
            return false;
        }

        resourceName = string.Concat(root, "/", CompiledName(path));
        return true;
    }

    private static string CompiledName(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());

        if (extension.IsEmpty)
        {
            return path;
        }

        var withoutExtension = path[..^extension.Length];
        var compiled = extension[1..] switch
        {
            var e when e.Equals("xml", StringComparison.OrdinalIgnoreCase) => ".vxml",
            var e when e.Equals("css", StringComparison.OrdinalIgnoreCase) => ".vcss",
            var e when e.Equals("js", StringComparison.OrdinalIgnoreCase) => ".vjs",
            var e when e.Equals("ts", StringComparison.OrdinalIgnoreCase) => ".vts",
            var e when e.Equals("svg", StringComparison.OrdinalIgnoreCase) => ".vsvg",
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
