using System.IO;
using System.Linq;

namespace ValveResourceFormat.IO;

/// <summary>
/// Where <see cref="ContentExporter"/> writes the files of an export.
/// </summary>
public enum ContentExportLayout
{
    /// <summary>
    /// Every file is written at its content path below <see cref="ContentExporter.OutputRoot"/>, so references between
    /// files resolve once the root is an addon or mod.
    /// </summary>
    ContentPaths,

    /// <summary>
    /// The exported files are written to <see cref="ContentExporter.TargetFolder"/> without the content folder they
    /// share, and their dependencies as <see cref="ContentExportSettings.DependencyPlacement"/> says, see
    /// <see cref="ContentPaths.Relocate"/>. The files a decompiled file names after itself take its new name, references
    /// to other files are not changed.
    /// </summary>
    TargetFolder,

    /// <summary>
    /// Files are placed as with <see cref="TargetFolder"/>, and references inside them to files of the same export are
    /// rewritten to where those are written, see <see cref="FileExtractOptions.ReferenceMap"/>.
    /// </summary>
    TargetFolderRewriteReferences,
}

/// <summary>
/// Which resources an export also exports because the exported files use them.
/// </summary>
public enum ContentExportDependencies
{
    /// <summary>No dependencies are exported.</summary>
    None,

    /// <summary>
    /// The resources the exported files reference, directly or through resources that only exist as part of them, such
    /// as the materials of a model's meshes.
    /// </summary>
    Direct,

    /// <summary>Every resource the exported files reference, directly or through other references.</summary>
    All,
}

/// <summary>
/// Where the dependencies of an export with a <see cref="ContentExportLayout.TargetFolder"/> layout are written.
/// </summary>
public enum ContentDependencyPlacement
{
    /// <summary>Below the target folder, at their full content path.</summary>
    WithMain,

    /// <summary>At their content path below <see cref="ContentExporter.OutputRoot"/>, as with <see cref="ContentExportLayout.ContentPaths"/>.</summary>
    OriginalPaths,

    /// <summary>
    /// At their content path below <see cref="ContentExporter.OutputRoot"/>, with <see cref="ContentExportSettings.DependencySuffix"/>
    /// added to their file name.
    /// </summary>
    Suffix,
}

/// <summary>
/// The settings of an export: how files are placed, what else is exported and the options of each decompiler. Every
/// setting is also a <see cref="ContentExportOption"/>, which reads and writes it as text.
/// </summary>
public sealed class ContentExportSettings
{
    /// <summary>The category of the options every export has.</summary>
    public const string GeneralCategory = "General";

    /// <summary>Gets or sets where the files are written.</summary>
    public ContentExportLayout Layout { get; set; }

    /// <summary>Gets or sets which resources the exported files use are exported too, from the same package, addon or mod only.</summary>
    public ContentExportDependencies Dependencies { get; set; }

    /// <summary>
    /// Gets or sets where dependencies are written with a target folder layout. Null renames them with
    /// <see cref="DependencySuffix"/> with a <see cref="ContentExportLayout.TargetFolderRewriteReferences"/> layout, and
    /// places them below the target folder otherwise.
    /// </summary>
    public ContentDependencyPlacement? DependencyPlacement { get; set; }

    /// <summary>
    /// Gets or sets the text added to the file names of dependencies with <see cref="ContentDependencyPlacement.Suffix"/>.
    /// Empty takes the text the target file name adds to the name of the main file, such as <c>_orig</c> for
    /// <c>haze.vmdl</c> saved as <c>haze_orig.vmdl</c>, or else <c>_copy</c>.
    /// </summary>
    public string DependencySuffix { get; set; } = string.Empty;

    /// <summary>Gets or sets what to do with files that existed before the export.</summary>
    public ExistingFileAction ExistingFiles { get; set; }

    /// <summary>Gets or sets the options resources are decompiled with.</summary>
    public FileExtractOptions ExtractOptions { get; set; } = new();

    /// <summary>Gets or sets <see cref="GltfModelExporter.ExportAnimations"/>.</summary>
    public bool GltfExportAnimations { get; set; } = true;

    /// <summary>Gets or sets <see cref="GltfModelExporter.ExportMaterials"/>.</summary>
    public bool GltfExportMaterials { get; set; } = true;

    /// <summary>Gets or sets <see cref="GltfModelExporter.AdaptTextures"/>.</summary>
    public bool GltfAdaptTextures { get; set; } = true;

    /// <summary>Gets or sets <see cref="GltfModelExporter.SatelliteImages"/>.</summary>
    public bool GltfSatelliteImages { get; set; } = true;

    /// <summary>Gets or sets <see cref="GltfModelExporter.ExportExtras"/>.</summary>
    public bool GltfExportExtras { get; set; }

    /// <summary>Gets or sets <see cref="GltfModelExporter.ComposeAdditiveAnimations"/>.</summary>
    public bool GltfComposeAdditiveAnimations { get; set; }

    /// <summary>Gets or sets <see cref="GltfModelExporter.ReduceAnimationKeyframes"/>.</summary>
    public bool GltfReduceAnimationKeyframes { get; set; } = true;

    /// <summary>Gets or sets <see cref="GltfModelExporter.ExportPhysics"/>.</summary>
    public bool GltfExportPhysics { get; set; } = true;

    /// <summary>Gets the option for <see cref="Layout"/>.</summary>
    public static ContentExportOption LayoutOption { get; } = ContentExportOption.Choice(
        "layout", "Output layout", GeneralCategory, "Where the exported files are written.", [],
        static settings => settings.Layout, static (settings, value) => settings.Layout = value,
        (ContentExportLayout.ContentPaths, "content", "Content paths (recommended)",
            "Files keep their game paths below the addon or mod folder, so references between them resolve."),
        (ContentExportLayout.TargetFolder, "folder", "Target folder",
            "Files go into the chosen folder or file name, references inside them are not changed."),
        (ContentExportLayout.TargetFolderRewriteReferences, "folder_rewrite", "Target folder with rewritten references (experimental)",
            "Files go into the chosen folder, references to files of this export point to their new paths."));

    /// <summary>Gets the option for <see cref="Dependencies"/>.</summary>
    public static ContentExportOption DependenciesOption { get; } = ContentExportOption.Choice(
        "dependencies", "Dependencies", GeneralCategory,
        "Also export the files the exported files use, from the same package, addon or mod.", [],
        static settings => settings.Dependencies, static (settings, value) => settings.Dependencies = value,
        (ContentExportDependencies.None, "none", "None", "Only the chosen files."),
        (ContentExportDependencies.Direct, "direct", "Direct", "The files the chosen files reference."),
        (ContentExportDependencies.All, "all", "All", "Every file the chosen files use, directly or through other files."));

    /// <summary>Gets the option for <see cref="DependencyPlacement"/>.</summary>
    public static ContentExportOption DependencyPlacementOption { get; } = ContentExportOption.Choice(
        "dependency_placement", "Dependency placement", GeneralCategory,
        "Where dependencies go with a target folder layout.", [],
        static settings => settings.ResolvedDependencyPlacement, static (settings, value) => settings.DependencyPlacement = value,
        (ContentDependencyPlacement.WithMain, "with_main", "In the target folder", "Below the target folder, at their full game path."),
        (ContentDependencyPlacement.OriginalPaths, "original", "At their game paths", "At their game path below the addon or mod folder."),
        (ContentDependencyPlacement.Suffix, "suffix", "At their game paths, renamed", "At their game path below the addon or mod folder, with the dependency suffix added to their file name."));

    /// <summary>Gets the option for <see cref="DependencySuffix"/>.</summary>
    public static ContentExportOption DependencySuffixOption { get; } = ContentExportOption.Text(
        "dependency_suffix", "Dependency suffix", GeneralCategory,
        "Added to the file names of renamed dependencies. Empty takes the text the new file name adds to the name of the chosen file, such as _orig for haze saved as haze_orig, or else _copy.", [],
        static settings => settings.DependencySuffix, static (settings, value) => settings.DependencySuffix = value,
        static value => value.AsSpan().IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) < 0);

    /// <summary>Gets the option for <see cref="ExistingFiles"/>.</summary>
    public static ContentExportOption ExistingFilesOption { get; } = ContentExportOption.Choice(
        "existing_files", "Existing files", GeneralCategory, "What to do with files that already exist.", [],
        static settings => settings.ExistingFiles, static (settings, value) => settings.ExistingFiles = value,
        (ExistingFileAction.Ask, "ask", "Ask", "Ask once, at the first file that already exists."),
        (ExistingFileAction.Overwrite, "overwrite", "Overwrite", "Replace existing files."),
        (ExistingFileAction.Skip, "skip", "Skip", "Keep existing files."));

    /// <summary>Gets the options every export has.</summary>
    public static IReadOnlyList<ContentExportOption> Options { get; } = [LayoutOption, DependenciesOption, DependencyPlacementOption, DependencySuffixOption, ExistingFilesOption];

    /// <summary>Gets where dependencies are written, see <see cref="DependencyPlacement"/>.</summary>
    public ContentDependencyPlacement ResolvedDependencyPlacement
        => DependencyPlacement ?? (Layout == ContentExportLayout.TargetFolderRewriteReferences ? ContentDependencyPlacement.Suffix : ContentDependencyPlacement.WithMain);

    /// <summary>Sets the glTF options of an exporter from these settings.</summary>
    internal void ApplyTo(GltfModelExporter exporter)
    {
        exporter.ExportAnimations = GltfExportAnimations;
        exporter.ExportMaterials = GltfExportMaterials;
        exporter.AdaptTextures = GltfAdaptTextures;
        exporter.SatelliteImages = GltfSatelliteImages;
        exporter.ExportExtras = GltfExportExtras;
        exporter.ComposeAdditiveAnimations = GltfComposeAdditiveAnimations;
        exporter.ReduceAnimationKeyframes = GltfReduceAnimationKeyframes;
        exporter.ExportPhysics = GltfExportPhysics;
    }

    /// <summary>
    /// Gets every option with its value as <c>id=value</c>, separated by spaces.
    /// </summary>
    public override string ToString()
        => string.Join(' ', ContentExportOption.All
            .Where(static option => option.Kind != ContentExportOptionKind.Notice)
            .Select(option => $"{option.Id}={option.GetValue(this)}"));
}
