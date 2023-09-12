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

    public static async Task<(bool IsNewVersionAvailable, bool IsStableBuild, string Version)> CheckForUpdates()
    {
        try
        {
            var version = Application.ProductVersion;
            var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit
            var currentVersion = new Version(versionPlus > 0 ? version[..versionPlus] : version);

            if (currentVersion.Build == 0)
            {
                return (false, false, string.Empty); // This was not built on the CI
            }

#if CI_RELEASE_BUILD
            const bool IsStableBuild = true;
            var updateUri = new Uri("https://api.github.com/repositories/42366054/releases/latest");
#else
            const bool IsStableBuild = false;
            var updateUri = new Uri("https://api.github.com/repositories/42366054/actions/runs?branch=master&status=success&per_page=1");
#endif

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Source 2 Viewer Update Check (+https://github.com/ValveResourceFormat/ValveResourceFormat)");

            var response = await httpClient.GetAsync(updateUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

#if CI_RELEASE_BUILD
            var json = await JsonSerializer.DeserializeAsync<GithubRelease>(jsonStream).ConfigureAwait(false);

            var newVersion = json?.tag_name ?? "0.0";

            if (newVersion.StartsWith("v", StringComparison.InvariantCulture)) // Just in case we tag a release with "v" prefix by mistake
            {
                newVersion = newVersion[1..];
            }

            var releaseVersion = new Version(newVersion);
            var updateAvailable = releaseVersion.Major > currentVersion.Major || releaseVersion.Minor > currentVersion.Minor;
#else
            var json = await JsonSerializer.DeserializeAsync<GithubActionRuns>(jsonStream).ConfigureAwait(false);
            var newBuild = json?.workflow_runs[0]?.run_number ?? 0;
            var updateAvailable = newBuild > currentVersion.Build;
            var newVersion = newBuild.ToString(CultureInfo.InvariantCulture);
#endif

            return (updateAvailable, IsStableBuild, newVersion);
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Failed to check for updates: {e.Message}").ConfigureAwait(false);

            MessageBox.Show($"Failed to check for updates: {e.Message}");

            return (false, false, null);
        }
    }
}
