using System.IO;
using System.Linq;

namespace ValveResourceFormat.IO;

/// <summary>
/// What a <see cref="ContentExportOption"/> asks for.
/// </summary>
public enum ContentExportOptionKind
{
    /// <summary>On or off, with the values <c>true</c> and <c>false</c>.</summary>
    Toggle,

    /// <summary>One of <see cref="ContentExportOption.Choices"/>.</summary>
    Choice,

    /// <summary>Text to show, without a value.</summary>
    Notice,
}

/// <summary>
/// Describes one setting of <see cref="ContentExportSettings"/> for a user interface, and reads and writes it as text.
/// Each decompiler declares the options it reads next to its code, <see cref="All"/> lists every one.
/// </summary>
public sealed class ContentExportOption
{
    private readonly Func<ContentExportSettings, string> getValue;
    private readonly Func<ContentExportSettings, string, bool> trySetValue;

    private ContentExportOption(string id, string label, string category, string description, string[] fileExtensions,
        ContentExportOptionKind kind, Func<ContentExportSettings, string> getValue, Func<ContentExportSettings, string, bool> trySetValue)
    {
        Id = id;
        Label = label;
        Category = category;
        Description = description;
        FileExtensions = fileExtensions;
        Kind = kind;
        this.getValue = getValue;
        this.trySetValue = trySetValue;
    }

    /// <summary>Gets the name the option is stored and given on the command line as.</summary>
    public string Id { get; }

    /// <summary>Gets the short name to show.</summary>
    public string Label { get; }

    /// <summary>Gets the name of the group the option is shown in.</summary>
    public string Category { get; }

    /// <summary>Gets what the option does, or the text of a notice.</summary>
    public string Description { get; }

    /// <summary>
    /// Gets the extensions, without their dot, of the files the option applies to, such as <c>vmdl_c</c>. Empty for an
    /// option of every export.
    /// </summary>
    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Gets whether the option only applies to files exported as glTF.</summary>
    public bool GltfOnly { get; private init; }

    /// <summary>Gets what the option asks for.</summary>
    public ContentExportOptionKind Kind { get; }

    /// <summary>Gets the values of a <see cref="ContentExportOptionKind.Choice"/> option with their names to show.</summary>
    public IReadOnlyList<(string Value, string Label, string Description)> Choices { get; private init; } = [];

    /// <summary>Gets the value of the option in new settings.</summary>
    public string DefaultValue => GetValue(new ContentExportSettings());

    /// <summary>
    /// Gets every option: those of every export, then those of each decompiler.
    /// </summary>
    public static IReadOnlyList<ContentExportOption> All { get; } =
    [
        .. ContentExportSettings.Options,
        .. ModelExtract.ExportOptions,
        .. MapExtract.ExportOptions,
        .. TextureExtract.ExportOptions,
        .. ToolsAssetInfo.ToolsAssetInfo.ExportOptions,
        .. ShaderExtract.ExportOptions,
        .. GltfModelExporter.ExportOptions,
    ];

    /// <summary>
    /// Gets the options that apply to an export: those of every export, and those of the types of files in it.
    /// </summary>
    /// <param name="contentPaths">The content paths of the exported files, or only their extensions with a leading dot.</param>
    /// <param name="gltf">Whether any file is exported as glTF.</param>
    public static IEnumerable<ContentExportOption> GetRelevant(IEnumerable<string> contentPaths, bool gltf)
    {
        var extensions = contentPaths
            .Select(static path => Path.GetExtension(path).TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return All.Where(option => (gltf || !option.GltfOnly)
            && (option.FileExtensions.Count == 0 || option.FileExtensions.Any(extensions.Contains)));
    }

    /// <summary>
    /// Finds an option by its <see cref="Id"/>, ignoring case.
    /// </summary>
    public static ContentExportOption? Find(string id)
        => All.FirstOrDefault(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the value of the option in the given settings.
    /// </summary>
    /// <returns>The value, or an empty string for a notice.</returns>
    public string GetValue(ContentExportSettings settings) => getValue(settings);

    /// <summary>
    /// Sets the option in the given settings.
    /// </summary>
    /// <param name="settings">The settings to change.</param>
    /// <param name="value">The value, ignoring case.</param>
    /// <returns>False when the value is not one the option takes, or the option is a notice.</returns>
    public bool TrySetValue(ContentExportSettings settings, string value) => trySetValue(settings, value.Trim());

    /// <summary>
    /// Gets the values the option takes, separated by a slash, for a description of it.
    /// </summary>
    public string GetValuesText() => Kind switch
    {
        ContentExportOptionKind.Toggle => "true/false",
        ContentExportOptionKind.Choice => string.Join('/', Choices.Select(static choice => choice.Value)),
        _ => string.Empty,
    };

    internal static ContentExportOption Toggle(string id, string label, string category, string description, string[] fileExtensions,
        Func<ContentExportSettings, bool> get, Action<ContentExportSettings, bool> set, bool gltfOnly = false)
    {
        return new ContentExportOption(id, label, category, description, fileExtensions, ContentExportOptionKind.Toggle,
            settings => get(settings) ? "true" : "false",
            (settings, value) =>
            {
                if (!bool.TryParse(value, out var parsed))
                {
                    return false;
                }

                set(settings, parsed);
                return true;
            })
        {
            GltfOnly = gltfOnly,
        };
    }

    internal static ContentExportOption Choice<T>(string id, string label, string category, string description, string[] fileExtensions,
        Func<ContentExportSettings, T> get, Action<ContentExportSettings, T> set, params (T Value, string Name, string Label, string Description)[] choices)
        where T : struct, Enum
    {
        return new ContentExportOption(id, label, category, description, fileExtensions, ContentExportOptionKind.Choice,
            settings =>
            {
                var value = get(settings);
                var index = Array.FindIndex(choices, choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
                return index >= 0 ? choices[index].Name : string.Empty;
            },
            (settings, value) =>
            {
                var index = Array.FindIndex(choices, choice => choice.Name.Equals(value, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                {
                    return false;
                }

                set(settings, choices[index].Value);
                return true;
            })
        {
            Choices = [.. choices.Select(static choice => (choice.Name, choice.Label, choice.Description))],
        };
    }

    internal static ContentExportOption Notice(string id, string label, string category, string text, string[] fileExtensions)
    {
        return new ContentExportOption(id, label, category, text, fileExtensions, ContentExportOptionKind.Notice,
            static _ => string.Empty, static (_, _) => false);
    }
}
