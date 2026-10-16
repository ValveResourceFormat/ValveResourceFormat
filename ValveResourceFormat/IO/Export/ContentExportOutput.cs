namespace ValveResourceFormat.IO;

/// <summary>
/// What a <see cref="ContentExportItem"/> is exported as.
/// </summary>
public enum ContentExportOutput
{
    /// <summary>
    /// A compiled resource is decompiled, and a file that is not one is extracted when its format is known, see
    /// <see cref="FileExtract.ExtractNonResource"/>. The files of a compiled shader in a package are decompiled to one
    /// <c>.vfx</c> file. Anything else is written as it is.
    /// </summary>
    Decompile,

    /// <summary>The file is written as it is.</summary>
    Raw,

    /// <summary>
    /// A resource that can be exported to glTF, see <see cref="GltfModelExporter.CanExport(Resource)"/>, and a
    /// navigation mesh are exported to a <c>.gltf</c> file next to where the decompiled file would go, everything else
    /// is decompiled. Requires <see cref="ContentExporter.GltfExporterFactory"/>.
    /// </summary>
    Gltf,

    /// <summary>The same as <see cref="Gltf"/>, as a binary <c>.glb</c> file.</summary>
    Glb,
}
