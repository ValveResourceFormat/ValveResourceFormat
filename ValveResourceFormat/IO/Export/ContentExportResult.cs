namespace ValveResourceFormat.IO;

/// <summary>
/// What happened to a <see cref="ContentExportItem"/> in a <see cref="ContentExportQueue"/>.
/// </summary>
public enum ContentExportResult
{
    /// <summary>The file was not exported, because the export was cancelled before it.</summary>
    NotExported,

    /// <summary>The file and every file extracted from it were written, or kept as they already existed.</summary>
    Exported,

    /// <summary>The file was skipped, because it was already exported or an exported resource consumed it.</summary>
    Handled,

    /// <summary>The file, or a file extracted from it, failed to export.</summary>
    Failed,
}
