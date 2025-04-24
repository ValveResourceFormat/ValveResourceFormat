//#define CI_RELEASE_BUILD // for testing. For CI builds, it is set in Directory.Build.props

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Utils;

#nullable disable

static partial class UpdateChecker
{
    public class GithubRelease
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
    }

    public class GithubActionRuns
    {
        public class Run
        {
            public int run_number { get; set; }
        }

        public Run[] workflow_runs { get; set; }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(GithubActionRuns))]
    [JsonSerializable(typeof(GithubRelease))]
    partial class SourceGenerationContext : JsonSerializerContext
    {
    }


    private static bool Checked;
    public static bool IsNewVersionAvailable { get; private set; }
    public static bool IsNewVersionStableBuild { get; private set; }
    public static string NewVersion { get; private set; }
    public static string ReleaseNotesUrl { get; private set; }
    public static string ReleaseNotesVersion { get; private set; }

    public static async Task CheckForUpdates()
    {
        if (Checked)
        {
            return;
        }

        Checked = true;

        try
        {
            var version = Application.ProductVersion;
            var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit
            var currentVersion = new Version(versionPlus > 0 ? version[..versionPlus] : version);

            if (currentVersion.Build == 0)
            {
                return; // This was not built on the CI
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Source 2 Viewer Update Check (+https://github.com/ValveResourceFormat/ValveResourceFormat)");

            var stableReleaseTask = GetLastStableRelease(httpClient);

#if !CI_RELEASE_BUILD
            // Fire the dev request away, before awaiting the first request for stable release
            var lastDevBuild = GetLastDevBuild(httpClient);
#endif

            var stableReleaseData = await stableReleaseTask.ConfigureAwait(false);
            var newVersion = stableReleaseData?.tag_name ?? "0.0";

            if (newVersion.StartsWith('v')) // Just in case we tag a release with "v" prefix by mistake
            {
                newVersion = newVersion[1..];
            }

            var releaseVersion = new Version(newVersion);
            IsNewVersionStableBuild = true;
            IsNewVersionAvailable = releaseVersion.Major > currentVersion.Major || releaseVersion.Minor > currentVersion.Minor;
            ReleaseNotesUrl = stableReleaseData?.html_url;
            ReleaseNotesVersion = newVersion;
            NewVersion = newVersion;

#if !CI_RELEASE_BUILD
            var devBuildData = await lastDevBuild.ConfigureAwait(false);

            if (!IsNewVersionAvailable && devBuildData != null)
            {
                var newBuild = devBuildData?.workflow_runs[0]?.run_number ?? 0;
                IsNewVersionStableBuild = false;
                IsNewVersionAvailable = newBuild > currentVersion.Build;
                NewVersion = newBuild.ToString(CultureInfo.InvariantCulture);
            }
#endif

            if (Settings.Config.Update.CheckAutomatically)
            {
                Settings.Config.Update.UpdateAvailable = IsNewVersionAvailable;
            }
        }
        catch (Exception e)
        {
            Log.Error(nameof(UpdateChecker), $"Failed to check for updates: {e.Message}");

            MessageBox.Show($"Failed to check for updates: {e.Message}", "Update check failed");
        }
    }

    private static async Task<GithubRelease> GetLastStableRelease(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(new Uri("https://api.github.com/repositories/42366054/releases/latest")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync(jsonStream, SourceGenerationContext.Default.GithubRelease).ConfigureAwait(false);
    }

#if !CI_RELEASE_BUILD
    private static async Task<GithubActionRuns> GetLastDevBuild(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(new Uri("https://api.github.com/repositories/42366054/actions/runs?branch=master&status=success&per_page=1")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync(jsonStream, SourceGenerationContext.Default.GithubActionRuns).ConfigureAwait(false);
    }
#endif
}
