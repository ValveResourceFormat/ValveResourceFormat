//#define CI_RELEASE_BUILD // for testing. For CI builds, it is set in Directory.Build.props

using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Utils;

static class UpdateChecker
{
#pragma warning disable CA1812 // TODO: Source generator is not working
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

    /*
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(GithubActionRuns))]
    [JsonSerializable(typeof(GithubRelease))]
    partial class SourceGenerationContext : JsonSerializerContext
    {
    }
    */

    public static bool IsNewVersionAvailable { get; private set; }
    public static bool IsNewVersionStableBuild { get; private set; }
    public static string NewVersion { get; private set; }
    public static string ReleaseNotesUrl { get; private set; }
    public static string ReleaseNotesVersion { get; private set; }

    public static async Task CheckForUpdates()
    {
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

#if CI_RELEASE_BUILD
            GithubActionRuns unstableBuildData = null;
#else
            var lastUnstableBuild = GetLastUnstableBuild(httpClient);
            var unstableBuildData = await lastUnstableBuild.ConfigureAwait(false);
#endif

            var stableReleaseData = await stableReleaseTask.ConfigureAwait(false);
            var newVersion = stableReleaseData?.tag_name ?? "0.0";

            if (newVersion.StartsWith("v", StringComparison.InvariantCulture)) // Just in case we tag a release with "v" prefix by mistake
            {
                newVersion = newVersion[1..];
            }

            var releaseVersion = new Version(newVersion);
            IsNewVersionStableBuild = true;
            IsNewVersionAvailable = releaseVersion.Major > currentVersion.Major || releaseVersion.Minor > currentVersion.Minor;
            ReleaseNotesUrl = stableReleaseData?.html_url;
            ReleaseNotesVersion = newVersion;
            NewVersion = newVersion;

            if (IsNewVersionAvailable)
            {
                return;
            }

            if (unstableBuildData != null)
            {
                var newBuild = unstableBuildData?.workflow_runs[0]?.run_number ?? 0;
                IsNewVersionStableBuild = false;
                IsNewVersionAvailable = newBuild > currentVersion.Build;
                NewVersion = newBuild.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Failed to check for updates: {e.Message}").ConfigureAwait(false);

            MessageBox.Show($"Failed to check for updates: {e.Message}");
        }
    }

    private static async Task<GithubRelease> GetLastStableRelease(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(new Uri("https://api.github.com/repositories/42366054/releases/latest")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync<GithubRelease>(jsonStream).ConfigureAwait(false);
    }

#if !CI_RELEASE_BUILD
    private static async Task<GithubActionRuns> GetLastUnstableBuild(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(new Uri("https://api.github.com/repositories/42366054/actions/runs?branch=master&status=success&per_page=1")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync<GithubActionRuns>(jsonStream).ConfigureAwait(false);
    }
#endif
}
