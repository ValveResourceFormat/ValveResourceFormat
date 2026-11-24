namespace ValveResourceFormat.IO;

/// <summary>
/// What to do when an export would replace a file that existed before it started.
/// </summary>
public enum ExistingFileAction
{
    /// <summary>
    /// Ask <see cref="ContentExporter.ConfirmExistingFiles"/> once, at the first file that exists, or replace the files
    /// when it is not set.
    /// </summary>
    Ask,

    /// <summary>Replace the existing file.</summary>
    Overwrite,

    /// <summary>Keep the existing file.</summary>
    Skip,

    /// <summary>Stop the export by throwing <see cref="OperationCanceledException"/>.</summary>
    Cancel,
}
