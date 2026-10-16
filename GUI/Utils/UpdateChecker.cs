//#define TEST_NON_LOCAL_BUILD // Pretend to have been built on a CI as a dev version

using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Utils;

static partial class UpdateChecker
{
    /// <summary>
    /// A downloadable file for one runtime identifier.
    /// </summary>
    public class UpdateAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    /// <summary>
    /// The latest tagged release.
    /// </summary>
    public class StableUpdate
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("releaseNotesUrl")]
        public string? ReleaseNotesUrl { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The latest automated build of the master branch.
    /// </summary>
    public class DevUpdate
    {
        [JsonPropertyName("buildNumber")]
        public int BuildNumber { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The update manifest describing every channel we can offer.
    /// </summary>
    public class UpdateManifest
    {
        [JsonPropertyName("stable")]
        public StableUpdate? Stable { get; set; }

        [JsonPropertyName("dev")]
        public DevUpdate? Dev { get; set; }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(UpdateManifest))]
    partial class SourceGenerationContext : JsonSerializerContext
    {
    }

    private const string ManifestUrl = "https://update.s2v.app/v1/latest.json";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan FailedCheckRetryDelay = TimeSpan.FromMinutes(30);

    // The manifest describes both channels, so it is fetched once and re-evaluated when the channel changes
    private static Task<UpdateManifest?>? ManifestTask;
    private static readonly Lock CheckLock = new();
    private static bool ErrorCheckStarted;
    private static bool CheckedThisSession;
    /// <summary>Whether the selected channel offers a build to install, either newer or a switch to that channel.</summary>
    public static bool IsNewVersionAvailable { get; private set; }
    /// <summary>Whether the offered build is newer than the running one.</summary>
    public static bool IsNewer { get; private set; }
    public static bool IsNewVersionStableBuild { get; private set; }
    public static string? NewVersion { get; private set; }
    /// <summary>The offered version as shown to the user, e.g. "20.0" or "dev build 7125".</summary>
    public static string? NewVersionText { get; private set; }
    public static string? ReleaseNotesUrl { get; private set; }
    public static string? ReleaseNotesVersion { get; private set; }
    public static string? DownloadUrl { get; private set; }
    public static long? DownloadSize { get; private set; }
    public static string? DownloadSha256 { get; private set; }

    /// <summary>How the running build compares to the releases recorded by the last successful update check.</summary>
    public enum UpdateStatus
    {
        /// <summary>No newer build is recorded, and no update check has completed this session to confirm it.</summary>
        Unknown,
        /// <summary>This build was not produced by the CI, so there is nothing to compare against.</summary>
        LocalBuild,
        /// <summary>An update check this session found the running build at least as new as both channels.</summary>
        UpToDate,
        /// <summary>A newer stable release exists.</summary>
        NewerStable,
        /// <summary>A newer dev build exists.</summary>
        NewerDev,
    }

    /// <summary>
    /// Whether the selected channel has a build newer than the running one, according to the last successful update check.
    /// </summary>
    public static bool IsUpdateAvailable => Settings.Config.Update.Channel == Settings.UpdateChannel.Stable
        ? GetUpdateStatus() == UpdateStatus.NewerStable
        : GetUpdateStatus() == UpdateStatus.NewerDev;

    /// <summary>
    /// Compares the running build to the releases recorded by the last successful update check.
    /// Never performs a request, so it is safe to consult from error handlers.
    /// </summary>
    public static UpdateStatus GetUpdateStatus()
    {
        var currentVersion = GetCurrentVersion();

        if (IsLocalBuild(currentVersion))
        {
            return UpdateStatus.LocalBuild;
        }

        var state = Settings.Config.Update;
        var stableIsNewer = IsStableNewer(state.LatestStableVersion, currentVersion);
        var devIsNewer = IsDevNewer(state.LatestDevBuild, currentVersion);

        if (state.Channel == Settings.UpdateChannel.Stable && stableIsNewer)
        {
            return UpdateStatus.NewerStable;
        }

        if (devIsNewer)
        {
            return UpdateStatus.NewerDev;
        }

        if (stableIsNewer)
        {
            return UpdateStatus.NewerStable;
        }

        // A release may have come out since the recorded check
        return CheckedThisSession ? UpdateStatus.UpToDate : UpdateStatus.Unknown;
    }

    /// <summary>
    /// Fetches the update manifest once per session after an error, if automatic checks are enabled, so that the recorded state is current.
    /// Reuses the manifest when this session already fetched it. Never throws.
    /// </summary>
    public static void CheckAfterError()
    {
        if (!Settings.Config.Update.CheckAutomatically)
        {
            return;
        }

        using (CheckLock.EnterScope())
        {
            if (ErrorCheckStarted)
            {
                return;
            }

            ErrorCheckStarted = true;
        }

        _ = CheckAfterErrorAsync();
    }

    private static async Task CheckAfterErrorAsync()
    {
        try
        {
            await CheckForUpdates().ConfigureAwait(false);

            ErrorReport.RefreshUpdateBadge();
        }
        catch (Exception e)
        {
            Log.Error(nameof(UpdateChecker), $"Failed to check for updates after an error: {e.Message}");
        }
    }

    public static async Task CheckForUpdates()
    {
        Task<UpdateManifest?> manifestTask;

        using (CheckLock.EnterScope())
        {
            // Offloaded so that the request setup does not run on the ui thread
            manifestTask = ManifestTask ??= Task.Run(GetManifestAsync);
        }

        UpdateManifest? manifest;

        try
        {
            manifest = await manifestTask.ConfigureAwait(false);
        }
        catch
        {
            // Let the next check retry instead of remembering the failure for the rest of the session
            using (CheckLock.EnterScope())
            {
                if (ManifestTask == manifestTask)
                {
                    ManifestTask = null;
                }
            }

            throw;
        }

        Evaluate(manifest);
    }

    /// <summary>
    /// Performs the automatic update check if it is enabled and due. The outcome is persisted in the settings.
    /// </summary>
    public static async Task CheckForUpdatesIfNecessary()
    {
        if (!Settings.Config.Update.CheckAutomatically || IsUpdateAvailable)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (DateTime.TryParseExact(Settings.Config.Update.NextCheck, "s", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextCheck) && now < nextCheck)
        {
            return;
        }

        try
        {
            await CheckForUpdates().ConfigureAwait(false);

            Settings.Config.Update.NextCheck = (now + CheckInterval).ToString("s", CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            // Being offline should not interrupt startup, a manual check from the About dialog reports its errors
            Log.Error(nameof(UpdateChecker), $"Failed to check for updates: {e.Message}");
            Settings.Config.Update.NextCheck = (now + FailedCheckRetryDelay).ToString("s", CultureInfo.InvariantCulture);
        }
    }

    private static Version GetCurrentVersion()
    {
        var version = Program.ProductVersion;
        var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit

        return new Version(versionPlus > 0 ? version[..versionPlus] : version);
    }

    private static bool IsLocalBuild(Version currentVersion)
    {
#if TEST_NON_LOCAL_BUILD
        return false;
#else
        return currentVersion.Build == 0;
#endif
    }

    private static bool IsStableNewer(string? stableVersion, Version currentVersion)
    {
        var releaseVersion = Version.TryParse(stableVersion, out var parsed) ? parsed : new Version(0, 0);

        return releaseVersion > new Version(currentVersion.Major, currentVersion.Minor);
    }

    // Tag and branch builds share one run number sequence, so this compares across channels too
    private static bool IsDevNewer(int devBuild, Version currentVersion)
    {
        return devBuild > 0 && devBuild > currentVersion.Build;
    }

    private static void Evaluate(UpdateManifest? manifest)
    {
        var currentVersion = GetCurrentVersion();

        if (IsLocalBuild(currentVersion))
        {
            IsNewVersionAvailable = false;
            IsNewVersionStableBuild = true;
            NewVersion = ":)";
            NewVersionText = NewVersion;
            return; // This was not built on the CI
        }

        if (manifest == null)
        {
            return;
        }

        var stable = manifest.Stable;
        var dev = manifest.Dev;

        if (stable is { Version.Length: > 0 })
        {
            Settings.Config.Update.LatestStableVersion = stable.Version;
        }

        if (dev is { BuildNumber: > 0 })
        {
            Settings.Config.Update.LatestDevBuild = dev.BuildNumber;
        }

        CheckedThisSession = true;

        // Release notes are always for the stable release, no matter which channel is selected
        ReleaseNotesUrl = stable?.ReleaseNotesUrl;
        ReleaseNotesVersion = stable?.Version;

        var channel = Settings.Config.Update.Channel;
        IsNewVersionStableBuild = channel == Settings.UpdateChannel.Stable;
        NewVersion = null;
        IsNewer = false;
        Dictionary<string, UpdateAsset>? assets = null;

        if (IsNewVersionStableBuild)
        {
            if (stable is { Version.Length: > 0 })
            {
                NewVersion = stable.Version;
                IsNewer = IsStableNewer(stable.Version, currentVersion);
                assets = stable.Assets;
            }
        }
        else if (dev is { BuildNumber: > 0 })
        {
            NewVersion = dev.BuildNumber.ToString(CultureInfo.InvariantCulture);
            IsNewer = IsDevNewer(dev.BuildNumber, currentVersion);
            assets = dev.Assets;
        }

        // An older or equal build on the other channel is offered from the About dialog, but never announced
        IsNewVersionAvailable = IsNewer || (NewVersion != null && channel != Program.BuildChannel);
        NewVersionText = NewVersion == null ? "Not available" : IsNewVersionStableBuild ? NewVersion : $"dev build {NewVersion}";

        var asset = assets?.GetValueOrDefault(RuntimeInformation.RuntimeIdentifier);

        // The file is verified after download, but the request itself should not go out in the clear either
        DownloadUrl = asset?.Url?.StartsWith("https://", StringComparison.Ordinal) == true ? asset.Url : null;
        DownloadSize = asset?.Size;
        DownloadSha256 = asset?.Sha256;
    }

    private static async Task<UpdateManifest?> GetManifestAsync()
    {
        if (IsLocalBuild(GetCurrentVersion()))
        {
            return null; // Local builds have nothing to compare against
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/ValveResourceFormat/ValveResourceFormat)");

        using var response = await httpClient.GetAsync(new Uri(ManifestUrl)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync(jsonStream, SourceGenerationContext.Default.UpdateManifest).ConfigureAwait(false);
    }
}
