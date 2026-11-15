using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using Sigstore;
using Windows.Win32.Foundation;

namespace GUI.Utils;

/// <summary>
/// Downloads the build offered by <see cref="UpdateChecker"/>, verifies it against the manifest and CI build provenance,
/// and swaps it in for the running executable.
/// </summary>
static class UpdateInstaller
{
    private const string Repository = "ValveResourceFormat/ValveResourceFormat";
    private const string RepositoryId = "42366054";
    private const string Workflow = ".github/workflows/build.yml";
    private const string ProvenancePredicateType = "https://slsa.dev/provenance/v1";

    /// <summary>Starts the viewer as an elevated installer, followed by the download path and its SHA-256.</summary>
    public const string ElevatedInstallArgument = "--install-update";

    // Keeps the Sigstore trust root cached between verifications
    private static readonly SigstoreVerifier Verifier = new();

    /// <summary>The version that has been installed and takes effect on the next start, or null.</summary>
    public static string? InstalledVersionText { get; private set; }

    /// <summary>Whether an update is being downloaded or installed.</summary>
    public static bool IsInstalling { get; private set; }

    /// <summary>
    /// Downloads and installs the offered build behind a progress dialog.
    /// </summary>
    public static async Task InstallAsync(IWin32Window owner)
    {
        // Called from the UI thread only
        if (IsInstalling)
        {
            throw new InvalidOperationException("An update is already being installed.");
        }

        IsInstalling = true;

        try
        {
            await InstallCoreAsync(owner).ConfigureAwait(true);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private static async Task InstallCoreAsync(IWin32Window owner)
    {
        // Another check can change the offered update while this one installs, so everything below uses this snapshot
        var offer = UpdateChecker.Offer;
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the path of the running executable.");

        // Only a download that can be verified is ever offered, there is no unverified fallback
        var build = offer?.Build;
        var download = offer?.Download;

        if (build == null || download == null)
        {
            throw new InvalidOperationException("The update does not provide a verifiable download.");
        }

        // A random name in the user's own temp folder cannot be planted ahead of time by anyone else
        var downloadPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var downloaded = false;

        using (var dialog = new GenericProgressForm { Text = $"Downloading {build.Text}" })
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
                    try
                    {
                        await DownloadVerifiedAsync(httpClient, build, download, downloadPath, dialog, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Older dev builds are removed once newer ones are published, the manifest names the one to get instead
                        dialog.SetProgress("Checking for a newer build…");
                        // The manifest request is shared with other checks, so cancelling only stops waiting for it
                        await UpdateChecker.RecheckForUpdates().WaitAsync(cancellationToken).ConfigureAwait(false);

                        var newerOffer = UpdateChecker.Offer;
                        var newerBuild = newerOffer?.Build;
                        var newerDownload = newerOffer?.Download;

                        if (newerBuild == null || newerDownload == null || newerDownload.Url == download.Url)
                        {
                            throw;
                        }

                        build = newerBuild;
                        download = newerDownload;

                        await DownloadVerifiedAsync(httpClient, build, download, downloadPath, dialog, cancellationToken).ConfigureAwait(false);
                    }

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

        if (!ExecutableSwap.IsSingleFileBundle(exePath))
        {
            // Nothing to swap for a non bundled build, leave the verified file for inspection
            await AppMessageDialogs.ShowMessageAsync(
                $"The update was downloaded and verified, but this build cannot replace itself.{Environment.NewLine}{Environment.NewLine}{downloadPath}",
                "Update downloaded").ConfigureAwait(true);

            return;
        }

        var installed = true;

        try
        {
            if (ExecutableSwap.IsDirectoryWritable(Path.GetDirectoryName(exePath)!))
            {
                // A single file bundle loads assemblies from the executable path on first use.
                // After the swap that path is the new build, so load System.Diagnostics.Process now, before Program.Restart needs it
                RuntimeHelpers.RunClassConstructor(typeof(Process).TypeHandle);

                ExecutableSwap.Swap(exePath, downloadPath, download.Sha256);
            }
            else
            {
                installed = await InstallElevatedAsync(exePath, downloadPath, download.Sha256).ConfigureAwait(true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The update was verified but could not be installed over {exePath}. {e.Message}", e);
        }
        finally
        {
            ExecutableSwap.TryDelete(downloadPath);
        }

        if (!installed)
        {
            return;
        }

        InstalledVersionText = build.Text;

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
            Program.Restart(exePath);
        }
    }

    // The provenance is looked up by the hash the manifest promises while the file downloads,
    // and covers the file once it is verified to have that hash
    private static async Task DownloadVerifiedAsync(HttpClient httpClient, UpdateChecker.UpdateBuild build, UpdateChecker.UpdateDownload download, string downloadPath, GenericProgressForm dialog, CancellationToken cancellationToken)
    {
        using var provenanceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var provenance = VerifyProvenanceAsync(httpClient, build, download, provenanceCancellation.Token);

        try
        {
            var (size, hash) = await DownloadAsync(httpClient, download, downloadPath, dialog, cancellationToken).ConfigureAwait(false);

            dialog.SetProgress("Verifying…");
            Verify(build, download, downloadPath, size, hash);
        }
        catch
        {
            // The download failure is the one to report, the provenance check only has to stop
            await provenanceCancellation.CancelAsync().ConfigureAwait(false);
            await provenance.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        dialog.SetProgress("Verifying build provenance…");
        await provenance.ConfigureAwait(false);
    }

    private static async Task<(long Size, string Hash)> DownloadAsync(HttpClient httpClient, UpdateChecker.UpdateDownload download, string downloadPath, GenericProgressForm dialog, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(download.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? download.Size ?? 0;
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
                dialog.SetProgress(string.Create(CultureInfo.InvariantCulture, $"{written / 1024f / 1024f:F1} MB of {totalMegabytes:F1} MB"));
            }
            else
            {
                dialog.SetProgress(string.Create(CultureInfo.InvariantCulture, $"{written / 1024f / 1024f:F1} MB"));
            }
        }

        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var file = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
        using var destination = new HashingProgressStream(file, ReportProgress);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        return (destination.BytesWritten, destination.GetHash());
    }

    // The manifest only names the file. The CI origin of the file is proven by the provenance attestation GitHub stores for
    // the file hash, signed through Sigstore with the identity of the workflow run that produced it.
    private static async Task VerifyProvenanceAsync(HttpClient httpClient, UpdateChecker.UpdateBuild build, UpdateChecker.UpdateDownload download, CancellationToken cancellationToken)
    {
        var hash = download.Sha256;
        var url = $"https://api.github.com/repositories/{RepositoryId}/attestations/sha256:{hash}?per_page=100&predicate_type={Uri.EscapeDataString(ProvenancePredicateType)}";
        using var response = await httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException("No build provenance was found for the downloaded file.");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GitHub is limiting requests from this network, try the update again later.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);

        // Dev builds are attested on the branch, releases on their tag
        var expectedRef = build.GitRef;
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

    private static void Verify(UpdateChecker.UpdateBuild build, UpdateChecker.UpdateDownload download, string downloadPath, long actualSize, string actualHash)
    {
        var expectedSize = download.Size;

        if (expectedSize != null && actualSize != expectedSize)
        {
            throw new InvalidDataException($"Downloaded {actualSize} bytes but expected {expectedSize} bytes.");
        }

        ExecutableSwap.VerifyHash(actualHash, download.Sha256);

        // The file is what the manifest promised, now make sure the manifest promised the right build
        var fileVersion = FileVersionInfo.GetVersionInfo(downloadPath).FileVersion;

        if (!Version.TryParse(fileVersion, out var version) || !build.Matches(version))
        {
            throw new InvalidDataException($"The downloaded file reports version {fileVersion} instead of {build.Text}.");
        }
    }

    // Returns false when the user declines the elevation prompt
    private static async Task<bool> InstallElevatedAsync(string exePath, string downloadPath, string expectedHash)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = $"{ElevatedInstallArgument} \"{downloadPath}\" {expectedHash}",
                UseShellExecute = true,
                Verb = "runas",
            }) ?? throw new IOException("Failed to start the elevated installer.");

            await process.WaitForExitAsync().ConfigureAwait(true);

            if (process.ExitCode != 0)
            {
                throw new IOException(Marshal.GetExceptionForHR(process.ExitCode)?.Message ?? $"The elevated installer failed with code {process.ExitCode}.");
            }

            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANCELLED)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs a downloaded update over the running executable, from the elevated instance started by <see cref="InstallAsync"/>.
    /// </summary>
    /// <returns>The process exit code, zero on success and the error HRESULT otherwise.</returns>
    public static int InstallElevated(string downloadPath, string expectedHash)
    {
        var exePath = Environment.ProcessPath!;

        try
        {
            ExecutableSwap.Swap(exePath, downloadPath, expectedHash);
        }
        catch (Exception e)
        {
            return e.HResult;
        }

        ExecutableSwap.DeleteReplacedOnReboot(exePath);

        return 0;
    }
}
