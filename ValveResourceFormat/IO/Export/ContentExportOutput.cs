using System.IO;

namespace ValveResourceFormat.IO;

/// <summary>
/// What a <see cref="ContentExportItem"/> is exported as.
/// </summary>
public enum ContentExportOutput
{
    /// <summary>
    /// A compiled resource is decompiled, and a file that is not one is extracted when its format is known, see
    /// <see cref="FileExtract.ExtractNonResource(Stream, string)"/>. A compiled resource is recognized by its
    /// contents when its name lacks the <c>_c</c> suffix. The files of a compiled shader in a package or in a folder on disk
    /// are decompiled to one <c>.vfx</c> file. Anything else is written as it is.
    /// </summary>
    Decompile,

    /// <summary>The file is written as it is.</summary>
    Raw,

    /// <summary>
    /// A resource that can be exported to glTF, see <see cref="GltfModelExporter.CanExport(Resource)"/>, and a
    /// navigation mesh are exported to a <c>.gltf</c> file next to where the decompiled file would go, everything else
    /// is decompiled, see <see cref="ContentExporter.CanExportGltf"/>. The glTF exporter comes from
    /// <see cref="ContentExporter.GltfExporterFactory"/>. Exported dependencies that can be exported to glTF are too.
    /// </summary>
    Gltf,

    /// <summary>The same as <see cref="Gltf"/>, as a binary <c>.glb</c> file.</summary>
    Glb,
}
