using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace GUI.Utils;

/// <summary>
/// Puts an executable in place of one that may be running, and removes the files the swap leaves behind.
/// </summary>
static class ExecutableSwap
{
    private const string ReplacedSuffix = ".old";
    private const string PendingSuffix = ".new";
    private const int SwapAttempts = 5;
    private static readonly TimeSpan SwapRetryDelay = TimeSpan.FromMilliseconds(500);

    // A single file publish has nothing but the bundle on disk. A debug build has the managed
    // assembly next to its apphost, and cannot be replaced by a single downloaded file.
    public static bool IsSingleFileBundle(string exePath) => !File.Exists(Path.ChangeExtension(exePath, ".dll"));

    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Probing is the only reliable check, a running executable that cannot be deleted
    // and a folder without write access both fail with access denied
    public static bool IsDirectoryWritable(string directory)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(directory, Path.GetRandomFileName()), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Removes the executable that a previous swap replaced, and any copy that never got swapped in
    public static void CleanupLeftovers()
    {
        var exePath = Environment.ProcessPath;

        if (exePath == null)
        {
            return;
        }

        TryDelete(exePath + PendingSuffix);

        // An elevated install schedules its replaced executable for deletion on reboot instead
        var replacedPath = exePath + ReplacedSuffix;

        if (File.Exists(replacedPath) && IsDirectoryWritable(Path.GetDirectoryName(exePath)!))
        {
            _ = DeleteReplacedAsync(replacedPath);
        }
    }

    // The previous instance is usually still exiting when this one starts, so keep trying for a while.
    // Whatever is still in use after that is removed by a later launch.
    private static async Task DeleteReplacedAsync(string path)
    {
        for (var attempt = 0; attempt < 30 && !TryDelete(path); attempt++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
        }
    }

    // The next launch cannot delete the replaced executable without elevation
    public static void DeleteReplacedOnReboot(string exePath)
    {
        _ = PInvoke.MoveFileEx(exePath + ReplacedSuffix, null, MOVE_FILE_FLAGS.MOVEFILE_DELAY_UNTIL_REBOOT);
    }

    public static void VerifyHash(string actualHash, string expectedHash)
    {
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded file does not match the expected hash.");
        }
    }

    // Windows allows renaming a running executable, only deleting or overwriting it is refused.
    // The old file is removed on the next launch, once nothing is running from it anymore.
    // The source is only checked against a hash when one is given.
    public static void Swap(string exePath, string sourcePath, string? expectedHash)
    {
        var replacedPath = exePath + ReplacedSuffix;
        var pendingPath = exePath + PendingSuffix;

        try
        {
            File.Delete(replacedPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException("A previous update is still in use, restart the viewer and try again.", e);
        }

        try
        {
            // Copy the source next to the executable first, so that any permission problem in the install folder
            // surfaces before the running executable is touched, and what remains are two renames on the same volume.
            // Unlike a move, a copy takes the permissions of the install folder and drops the Mark of the Web.
            // It is hashed again as it is written because the temp folder is writable by anything the user runs.
            using (var source = File.OpenRead(sourcePath))
            using (var file = new FileStream(pendingPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var destination = new HashingProgressStream(file, static _ => { }))
            {
                source.CopyTo(destination);

                if (expectedHash != null)
                {
                    VerifyHash(destination.GetHash(), expectedHash);
                }
            }

            var replacing = File.Exists(exePath);

            // Antivirus software often holds a freshly written executable open for a moment while it scans it,
            // so the swap is retried, with the running executable put back in between
            for (var attempt = 1; ; attempt++)
            {
                if (replacing)
                {
                    File.Move(exePath, replacedPath);
                }

                try
                {
                    File.Move(pendingPath, exePath);
                    break;
                }
                catch (Exception e)
                {
                    if (replacing)
                    {
                        RestoreReplaced(exePath, replacedPath, pendingPath, e);
                    }

                    var mayBeInUse = e is IOException or UnauthorizedAccessException;

                    if (!mayBeInUse || attempt == SwapAttempts)
                    {
                        throw;
                    }
                }

                Thread.Sleep(SwapRetryDelay);
            }
        }
        catch
        {
            // Without an executable in place, the pending copy is what the user can rename to recover
            if (File.Exists(exePath))
            {
                TryDelete(pendingPath);
            }

            throw;
        }
    }

    private static void RestoreReplaced(string exePath, string replacedPath, string pendingPath, Exception moveError)
    {
        try
        {
            File.Move(replacedPath, exePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{moveError.Message} The previous version could not be put back either, rename {pendingPath} to {Path.GetFileName(exePath)} to finish the update.", e);
        }
    }
}

// Hashes and counts bytes as they are written, so the file is verified and progress reported
// without reading it back.
sealed class HashingProgressStream(Stream inner, Action<long> onProgress) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long BytesWritten { get; private set; }

    public string GetHash() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        Advance(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(buffer.Span);
    }

    private void Advance(ReadOnlySpan<byte> buffer)
    {
        hash.AppendData(buffer);
        BytesWritten += buffer.Length;
        onProgress(BytesWritten);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
