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
    // Numbers each manifest fetch, so results are applied in the order the fetches started
    private static int FetchCount;
    private static int ManifestFetch;
    private static int AppliedFetch;
    private static bool ErrorCheckStarted;
    private static bool CheckedThisSession;
    private static volatile UpdateOffer? CurrentOffer;

    /// <summary>
    /// What the selected channel offers, as of the last evaluated manifest, or null before any check has completed.
    /// Read it once and use that instance, as a later check replaces it.
    /// </summary>
    public static UpdateOffer? Offer => CurrentOffer;

    /// <summary>
    /// A build of one update channel: a stable release by its tag, or a dev build by its CI build number.
    /// </summary>
    public sealed class UpdateBuild
    {
        /// <summary>The channel that publishes this build.</summary>
        public Settings.UpdateChannel Channel { get; private init; }

        /// <summary>Release tag of a stable build, e.g. "20.0", empty for a dev build.</summary>
        public string Tag { get; private init; } = string.Empty;

        /// <summary>Version parsed from <see cref="Tag"/>, 0.0 for a dev build.</summary>
        public Version Release { get; private init; } = new();

        /// <summary>CI build number of a dev build, 0 for a stable build.</summary>
        public int BuildNumber { get; private init; }

        private bool IsStable => Channel == Settings.UpdateChannel.Stable;

        /// <summary><see cref="BuildNumber"/> as an argument of a translated message.</summary>
        public string BuildNumberText => BuildNumber.ToString(CultureInfo.InvariantCulture);

        /// <summary>The build in English for logs and exceptions, e.g. "20.0" or "dev build 7125".</summary>
        public string Text => IsStable ? Tag : string.Create(CultureInfo.InvariantCulture, $"dev build {BuildNumber}");

        /// <summary>The build as a translated label, e.g. "20.0" or "dev build 7125".</summary>
        public string DisplayText => IsStable ? Tag : Locale.Update_DevBuild(BuildNumberText);

        /// <summary>The git ref the build is attested on.</summary>
        public string GitRef => IsStable ? $"refs/tags/{Tag}" : "refs/heads/master";

        /// <summary>Whether this build is newer than the given running version. Tag and branch builds share one build number sequence.</summary>
        public bool IsNewerThan(Version current) => IsStable
            ? Release > new Version(current.Major, current.Minor)
            : BuildNumber > current.Build;

        /// <summary>Whether the version of a downloaded file identifies this build.</summary>
        public bool Matches(Version fileVersion) => IsStable
            ? fileVersion.Major == Release.Major && fileVersion.Minor == Release.Minor
            : fileVersion.Build == BuildNumber;

        /// <summary>Creates a stable build from a release tag, or null when it is not a version.</summary>
        public static UpdateBuild? FromStable(string? tag)
        {
            if (!Version.TryParse(tag, out var release))
            {
                return null;
            }

            return new UpdateBuild { Channel = Settings.UpdateChannel.Stable, Tag = tag, Release = release };
        }

        /// <summary>Creates a dev build, or null when the build number is not positive.</summary>
        public static UpdateBuild? FromDev(int buildNumber)
        {
            if (buildNumber <= 0)
            {
                return null;
            }

            return new UpdateBuild { Channel = Settings.UpdateChannel.Dev, BuildNumber = buildNumber };
        }
    }

    /// <summary>
    /// A consistent view of the build offered by one evaluation of the manifest.
    /// </summary>
    public sealed class UpdateOffer
    {
        /// <summary>Whether the selected channel offers a build to install, either newer or a switch to that channel.</summary>
        public bool IsNewVersionAvailable { get; init; }
        /// <summary>Whether the offered build is newer than the running one.</summary>
        public bool IsNewer { get; init; }
        /// <summary>The build the selected channel offers, or null when it has none.</summary>
        public UpdateBuild? Build { get; init; }
        public string? ReleaseNotesUrl { get; init; }
        public string? ReleaseNotesVersion { get; init; }
        /// <summary>The file to install, or null when the manifest does not give a download that can be verified.</summary>
        public UpdateDownload? Download { get; init; }
    }

    /// <summary>
    /// A file offered for installation, with the hash it must match.
    /// </summary>
    public sealed record UpdateDownload(string Url, string Sha256, long? Size);

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
    public static bool IsUpdateAvailable => GetNewerBuild()?.Channel == Settings.Config.Update.Channel;

    /// <summary>
    /// The newest build newer than the running one according to the last successful update check, or null.
    /// The selected channel is preferred when it has a newer stable release. Never performs a request.
    /// </summary>
    public static UpdateBuild? GetNewerBuild()
    {
        if (IsLocalBuild)
        {
            return null;
        }

        var currentVersion = Program.Version;

        var state = Settings.Config.Update;
        var stable = UpdateBuild.FromStable(state.LatestStableVersion);
        var dev = UpdateBuild.FromDev(state.LatestDevBuild);
        var stableIsNewer = stable != null && stable.IsNewerThan(currentVersion);

        if (state.Channel == Settings.UpdateChannel.Stable && stableIsNewer)
        {
            return stable;
        }

        if (dev != null && dev.IsNewerThan(currentVersion))
        {
            return dev;
        }

        return stableIsNewer ? stable : null;
    }

    /// <summary>
    /// Compares the running build to the releases recorded by the last successful update check.
    /// Never performs a request, so it is safe to consult from error handlers.
    /// </summary>
    public static UpdateStatus GetUpdateStatus()
    {
        if (IsLocalBuild)
        {
            return UpdateStatus.LocalBuild;
        }

        var newerBuild = GetNewerBuild();

        if (newerBuild != null)
        {
            return newerBuild.Channel == Settings.UpdateChannel.Stable ? UpdateStatus.NewerStable : UpdateStatus.NewerDev;
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
        int fetch;

        using (CheckLock.EnterScope())
        {
            if (ManifestTask == null)
            {
                // Offloaded so that the request setup does not run on the ui thread
                ManifestTask = Task.Run(GetManifestAsync);
                ManifestFetch = ++FetchCount;
            }

            manifestTask = ManifestTask;
            fetch = ManifestFetch;
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

        Evaluate(manifest, fetch);
    }

    /// <summary>
    /// Fetches the manifest again instead of reusing the one fetched earlier this session.
    /// </summary>
    public static async Task RecheckForUpdates()
    {
        using (CheckLock.EnterScope())
        {
            ManifestTask = null;
        }

        await CheckForUpdates().ConfigureAwait(false);
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

    private static bool IsLocalBuild =>
#if TEST_NON_LOCAL_BUILD
        false;
#else
        Program.Version.Build == 0;
#endif

    private static void Evaluate(UpdateManifest? manifest, int fetch)
    {
        using (CheckLock.EnterScope())
        {
            // Fetches can complete out of order, an older one must not overwrite a newer result
            if (fetch < AppliedFetch)
            {
                return;
            }

            AppliedFetch = fetch;

            if (manifest == null)
            {
                return;
            }

            RecordManifest(manifest);
            CurrentOffer = CreateOffer(manifest);
        }
    }

    // Remembers the latest builds, so the status can be read without a request
    private static void RecordManifest(UpdateManifest manifest)
    {
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
    }

    private static UpdateOffer CreateOffer(UpdateManifest manifest)
    {
        var stable = manifest.Stable;
        var dev = manifest.Dev;
        var channel = Settings.Config.Update.Channel;
        UpdateBuild? build;
        Dictionary<string, UpdateAsset>? assets;

        if (channel == Settings.UpdateChannel.Stable)
        {
            build = UpdateBuild.FromStable(stable?.Version);
            assets = stable?.Assets;
        }
        else
        {
            build = UpdateBuild.FromDev(dev?.BuildNumber ?? 0);
            assets = dev?.Assets;
        }

        var isNewer = build != null && build.IsNewerThan(Program.Version);
        var asset = assets?.GetValueOrDefault(RuntimeInformation.RuntimeIdentifier);

        // The file is verified after download, but the request itself should not go out in the clear either
        UpdateDownload? download = null;

        if (asset?.Url != null && asset.Sha256 != null && asset.Url.StartsWith("https://", StringComparison.Ordinal))
        {
            download = new UpdateDownload(asset.Url, asset.Sha256, asset.Size);
        }

        return new UpdateOffer
        {
            // An older or equal build on the other channel is offered from the About dialog, but never announced
            IsNewVersionAvailable = isNewer || (build != null && build.Channel != Program.BuildChannel),
            IsNewer = isNewer,
            Build = build,
            // Release notes are always for the stable release, no matter which channel is selected
            ReleaseNotesUrl = stable?.ReleaseNotesUrl,
            ReleaseNotesVersion = stable?.Version,
            Download = download,
        };
    }

    private static async Task<UpdateManifest?> GetManifestAsync()
    {
        if (IsLocalBuild)
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
