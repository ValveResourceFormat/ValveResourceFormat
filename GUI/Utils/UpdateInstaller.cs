using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using Sigstore;

namespace GUI.Utils;

/// <summary>
/// Downloads the build offered by <see cref="UpdateChecker"/>, verifies it against the manifest and CI build provenance,
/// and swaps it in for the running executable.
/// </summary>
static class UpdateInstaller
{
    private const string ReplacedSuffix = ".old";
    private const string PendingSuffix = ".new";
    private const string Repository = "ValveResourceFormat/ValveResourceFormat";
    private const string RepositoryId = "42366054";
    private const string Workflow = ".github/workflows/build.yml";
    private const string ProvenancePredicateType = "https://slsa.dev/provenance/v1";

    // Keeps the Sigstore trust root cached between verifications
    private static readonly SigstoreVerifier Verifier = new();

    /// <summary>The version that has been installed and takes effect on the next start, or null.</summary>
    public static string? InstalledVersionText { get; private set; }

    // A single file publish has nothing but the bundle on disk. A debug build has the managed
    // assembly next to its apphost, and cannot be replaced by a single downloaded file.
    private static bool IsSingleFileBundle(string exePath) => !File.Exists(Path.ChangeExtension(exePath, ".dll"));

    /// <summary>
    /// Removes the executable that a previous update replaced, and any download that never got swapped in.
    /// </summary>
    public static void CleanupPreviousInstall()
    {
        var exePath = Environment.ProcessPath;

        if (exePath == null)
        {
            return;
        }

        TryDelete(exePath + PendingSuffix);
        _ = DeleteReplacedAsync(exePath + ReplacedSuffix);
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

    private static bool TryDelete(string path)
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

    /// <summary>
    /// Downloads and installs the offered build behind a progress dialog.
    /// </summary>
    public static async Task InstallAsync(IWin32Window owner)
    {
        var url = UpdateChecker.DownloadUrl;
        var expectedHash = UpdateChecker.DownloadSha256;
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the path of the running executable.");

        // Only a download that can be verified is ever offered, there is no unverified fallback
        if (url == null || expectedHash == null)
        {
            throw new InvalidOperationException("The update does not provide a verifiable download.");
        }

        // A random name in the user's own temp folder cannot be planted ahead of time by anyone else
        var downloadPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var downloaded = false;

        using (var dialog = new GenericProgressForm { Text = $"Downloading {UpdateChecker.NewVersionText}" })
        {
            dialog.OnProcess = async cancellationToken =>
            {
                using var httpClient = new HttpClient
                {
                    // Downloads can take a while on a slow connection, the dialog's cancel button is the way out
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/{Repository})");

                try
                {
                    var (size, hash) = await DownloadAsync(httpClient, url, downloadPath, dialog, cancellationToken).ConfigureAwait(false);

                    dialog.SetProgress("Verifying…");
                    Verify(downloadPath, size, hash, expectedHash);

                    dialog.SetProgress("Verifying build provenance…");
                    await VerifyProvenanceAsync(httpClient, hash, cancellationToken).ConfigureAwait(false);

                    downloaded = true;
                }
                catch
                {
                    File.Delete(downloadPath);
                    throw;
                }
            };

            await dialog.ShowDialogAsync(owner).ConfigureAwait(true);
            await dialog.WorkCompletion.ConfigureAwait(true);
        }

        if (!downloaded)
        {
            return;
        }

        if (!IsSingleFileBundle(exePath))
        {
            // Nothing to swap for a non bundled build, leave the verified file for inspection
            await AppMessageDialogs.ShowMessageAsync(
                $"The update was downloaded and verified, but this build cannot replace itself.{Environment.NewLine}{Environment.NewLine}{downloadPath}",
                "Update downloaded").ConfigureAwait(true);

            return;
        }

        try
        {
            Swap(exePath, downloadPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(downloadPath);
            TryDelete(exePath + PendingSuffix);

            throw new IOException($"The update was verified but could not be installed over {exePath}. {e.Message}", e);
        }

        InstalledVersionText = UpdateChecker.NewVersionText;

        var restartButton = new TaskDialogButton("Restart now");
        var page = new TaskDialogPage
        {
            Caption = "Update installed",
            Heading = $"Source 2 Viewer {InstalledVersionText} has been installed",
            Text = "It will be used the next time the viewer starts.",
            Icon = TaskDialogIcon.ShieldSuccessGreenBar,
            Buttons = { restartButton, new TaskDialogButton("Later") },
            DefaultButton = restartButton,
        };

        if (await TaskDialog.ShowDialogAsync(owner, page).ConfigureAwait(true) == restartButton)
        {
            Restart();
        }
    }

    /// <summary>
    /// Closes this instance and starts the installed executable.
    /// </summary>
    public static void Restart()
    {
        var exePath = Environment.ProcessPath!;

        // Closing the main window saves the settings, so the new instance starts from the final state
        Program.MainForm.Close();

        if (!Program.MainForm.IsDisposed)
        {
            return; // Closing was cancelled
        }

        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    // Hashes and counts bytes as they are written, so the file is verified and progress reported
    // without reading it back.
    private sealed class HashingProgressStream(Stream inner, Action<long> onProgress) : Stream
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

    private static async Task<(long Size, string Hash)> DownloadAsync(HttpClient httpClient, string url, string downloadPath, GenericProgressForm dialog, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? UpdateChecker.DownloadSize ?? 0;
        var totalMegabytes = totalBytes / 1024f / 1024f;

        if (totalBytes > 0)
        {
            dialog.SetBarMax(1000);
        }

        void ReportProgress(long written)
        {
            if (totalBytes > 0)
            {
                dialog.SetBarValue((int)(written * 1000 / totalBytes));
                dialog.SetProgress($"{written / 1024f / 1024f:F1} MB of {totalMegabytes:F1} MB");
            }
            else
            {
                dialog.SetProgress($"{written / 1024f / 1024f:F1} MB");
            }
        }

        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var file = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
        using var destination = new HashingProgressStream(file, ReportProgress);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        return (destination.BytesWritten, destination.GetHash());
    }

    // The manifest only names the file. Its CI origin is proven by the provenance attestation GitHub stores for
    // its hash, signed through Sigstore with the identity of the workflow run that produced it.
    private static async Task VerifyProvenanceAsync(HttpClient httpClient, string hash, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repositories/{RepositoryId}/attestations/sha256:{hash}?per_page=100&predicate_type={Uri.EscapeDataString(ProvenancePredicateType)}";
        using var response = await httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidDataException("No build provenance was found for the downloaded file.");
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);

        // Dev builds are attested on the branch, releases on their tag
        var expectedRef = UpdateChecker.IsNewVersionStableBuild ? $"refs/tags/{UpdateChecker.NewVersion}" : "refs/heads/master";
        var policy = new VerificationPolicy
        {
            CertificateIdentity = new CertificateIdentity
            {
                Issuer = "https://token.actions.githubusercontent.com",
                SubjectAlternativeName = $"https://github.com/{Repository}/{Workflow}@{expectedRef}",
                Extensions = new CertificateExtensionPolicy
                {
                    SourceRepositoryUri = $"https://github.com/{Repository}",
                    SourceRepositoryIdentifier = RepositoryId,
                    SourceRepositoryRef = expectedRef,
                    RunnerEnvironment = "github-hosted",
                },
            },
        };

        var hashBytes = Convert.FromHexString(hash);
        var failure = "No build provenance was found for the downloaded file.";

        foreach (var attestation in document.RootElement.GetProperty("attestations").EnumerateArray())
        {
            // One unusable attestation must not stop the others from being tried
            try
            {
                var bundle = await LoadBundleAsync(httpClient, attestation, cancellationToken).ConfigureAwait(false);

                // The library proves who signed the statement, the statement itself must still be
                // build provenance that names our file
                var statement = bundle.DsseEnvelope?.GetStatement();

                if (statement?.PredicateType != ProvenancePredicateType)
                {
                    failure = "The attestation is not build provenance.";
                    continue;
                }

                if (!statement.Subject.Any(subject => subject.Digest.TryGetValue("sha256", out var digest) && digest.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                {
                    failure = "The attestation does not describe the downloaded file.";
                    continue;
                }

                var (success, result) = await Verifier.TryVerifyDigestAsync(hashBytes, HashAlgorithmType.Sha256, bundle, policy, cancellationToken).ConfigureAwait(false);

                if (success)
                {
                    Log.Info(nameof(UpdateInstaller), $"Verified build provenance signed by {result?.SignerIdentity}");
                    return;
                }

                failure = result?.FailureReason;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failure = e.Message;
            }
        }

        throw new InvalidDataException($"The build provenance of the downloaded file could not be verified. {failure}");
    }

    // The bundle is usually inlined, but the API also offers it as a separate download
    private static async Task<SigstoreBundle> LoadBundleAsync(HttpClient httpClient, JsonElement attestation, CancellationToken cancellationToken)
    {
        if (attestation.TryGetProperty("bundle", out var inline) && inline.ValueKind == JsonValueKind.Object)
        {
            return SigstoreBundle.Deserialize(inline.GetRawText());
        }

        var bundleUrl = attestation.GetProperty("bundle_url").GetString() ?? throw new InvalidDataException("The attestation has no bundle.");
        var json = await httpClient.GetStringAsync(new Uri(bundleUrl), cancellationToken).ConfigureAwait(false);

        return SigstoreBundle.Deserialize(json);
    }

    private static void Verify(string downloadPath, long actualSize, string actualHash, string expectedHash)
    {
        var expectedSize = UpdateChecker.DownloadSize;

        if (expectedSize != null && actualSize != expectedSize)
        {
            throw new InvalidDataException($"Downloaded {actualSize} bytes but expected {expectedSize} bytes.");
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded file does not match the expected hash.");
        }

        // The file is what the manifest promised, now make sure the manifest promised the right build
        var fileVersion = FileVersionInfo.GetVersionInfo(downloadPath).FileVersion;

        if (!Version.TryParse(fileVersion, out var version) || !Version.TryParse(UpdateChecker.NewVersion, out var expectedVersion))
        {
            // Dev builds are identified by build number alone
            if (!int.TryParse(UpdateChecker.NewVersion, out var expectedBuild) || version?.Build != expectedBuild)
            {
                throw new InvalidDataException($"The downloaded file reports version {fileVersion} instead of {UpdateChecker.NewVersion}.");
            }

            return;
        }

        if (version.Major != expectedVersion.Major || version.Minor != expectedVersion.Minor)
        {
            throw new InvalidDataException($"The downloaded file reports version {fileVersion} instead of {UpdateChecker.NewVersion}.");
        }
    }

    // Windows allows renaming a running executable, only deleting or overwriting it is refused.
    // The old file is removed on the next launch, once nothing is running from it anymore.
    private static void Swap(string exePath, string downloadPath)
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

        // Bring the download next to the executable first, so that the copy from the temp folder and any
        // permission problem in the install folder surface before the running executable is touched.
        // What remains are two renames on the same volume.
        File.Move(downloadPath, pendingPath, overwrite: true);
        File.Move(exePath, replacedPath);

        try
        {
            File.Move(pendingPath, exePath);
        }
        catch
        {
            File.Move(replacedPath, exePath);
            throw;
        }
    }
}
