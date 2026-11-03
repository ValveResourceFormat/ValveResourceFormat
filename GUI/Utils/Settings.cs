using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    /// <summary>
    /// Manages application settings.
    /// </summary>
    static class Settings
    {
        private const int SettingsFileCurrentVersion = 18;
        private const int RecentFilesLimit = 20;

        /// <summary>
        /// Flags that control quick file preview behavior in the file explorer.
        /// </summary>
        [Flags]
        public enum QuickPreviewFlags : int
        {
            /// <summary>Quick preview is enabled.</summary>
            Enabled = 1 << 0,
            /// <summary>Sounds are automatically played when previewing audio files.</summary>
            AutoPlaySounds = 1 << 1,
        }

        /// <summary>
        /// The channel that application updates are offered from.
        /// </summary>
        public enum UpdateChannel : int
        {
            /// <summary>Tagged stable releases.</summary>
            Stable = 0,
            /// <summary>Automated builds of the master branch.</summary>
            Dev = 1,
        }

        /// <summary>
        /// Holds state related to automatic application update checks.
        /// </summary>
        public class AppUpdateState
        {
            /// <summary>Gets or sets whether to automatically check for updates on startup.</summary>
            public bool CheckAutomatically { get; set; } = true;
            /// <summary>Gets or sets the channel that updates are offered from.</summary>
            public UpdateChannel Channel { get; set => field = Enum.IsDefined(value) ? value : UpdateChannel.Stable; }
            /// <summary>Gets or sets when the next automatic update check is due.</summary>
            public string NextCheck { get; set; } = string.Empty;
            /// <summary>Gets or sets the application version recorded the last time settings were loaded, used to detect version changes and reset update state.</summary>
            public string Version { get; set; } = string.Empty;
            /// <summary>Gets or sets the latest dev build number seen by the last successful update check, 0 when unknown.</summary>
            public int LatestDevBuild { get; set; }
            /// <summary>Gets or sets the latest stable release version seen by the last successful update check, empty when unknown.</summary>
            public string LatestStableVersion { get; set; } = string.Empty;
        }

        /// <summary>
        /// Represents the full set of persisted application configuration values.
        /// </summary>
        public class AppConfig
        {
            /// <summary>Gets or sets the list of game content search paths.</summary>
            public List<string> GameSearchPaths { get; set; } = [];
            /// <summary>Gets or sets the last directory used when opening files.</summary>
            public string OpenDirectory { get; set; } = string.Empty;
            /// <summary>Gets or sets the last directory used when saving files.</summary>
            public string SaveDirectory { get; set; } = string.Empty;
            /// <summary>Gets or sets the list of bookmarked file paths.</summary>
            public List<string> BookmarkedFiles { get; set; } = [];
            /// <summary>Gets or sets the list of recently opened file paths.</summary>
            public List<string> RecentFiles { get; set; } = [];
            /// <summary>Gets or sets saved camera positions keyed by name.</summary>
            public Dictionary<string, float[]> SavedCameras { get; set; } = [];
            /// <summary>Gets or sets the selected UI theme index.</summary>
            public int Theme { get; set; }
            /// <summary>Gets or sets the maximum texture resolution loaded by the renderer.</summary>
            public int MaxTextureSize { get; set => field = value <= 0 ? 1024 : Math.Min(value, 10240); } = 1024;
            /// <summary>Gets or sets the shadow map resolution.</summary>
            public int ShadowResolution { get; set => field = value <= 0 ? 2048 : Math.Min(value, 4096); } = 2048;
            /// <summary>Gets or sets the camera field of view in degrees.</summary>
            public float FieldOfView { get; set => field = value <= 0 ? 90 : Math.Clamp(value, 1, 170); } = 90;
            /// <summary>Gets or sets the first-person viewmodel field of view in degrees.</summary>
            public float ViewmodelFieldOfView { get; set => field = Math.Clamp(value, 40, 80); } = 64;
            /// <summary>Gets or sets the mouse look sensitivity.</summary>
            public float MouseSensitivity { get; set => field = Math.Clamp(value, 0, 8); } = 4f;
            /// <summary>Gets or sets whether the viewport camera should have acceleration/deceleration when starting or stopping to move</summary>
            public bool SmoothCameraEnabled { get; set; } = true;
            /// <summary>Gets or sets the number of MSAA samples used for anti-aliasing.</summary>
            public int AntiAliasingSamples { get; set => field = Math.Clamp(value, 0, 64); } = 8;
            /// <summary>Gets or sets the top edge position of the main window.</summary>
            public int WindowTop { get; set; }
            /// <summary>Gets or sets the left edge position of the main window.</summary>
            public int WindowLeft { get; set; }
            /// <summary>Gets or sets the width of the main window.</summary>
            public int WindowWidth { get; set; }
            /// <summary>Gets or sets the height of the main window.</summary>
            public int WindowHeight { get; set; }
            /// <summary>Gets or sets the window state (normal, minimized, maximized).</summary>
            public int WindowState { get; set; }
            /// <summary>Gets or sets the normalized audio playback volume.</summary>
            public float Volume { get; set => field = MathUtils.Saturate(value); } = 0.5f;
            /// <summary>Gets or sets the swap interval (the number of screen updates to wait between swapping front and back buffers).</summary>
            public int Vsync { get; set; } = 1;
            /// <summary>Gets or sets whether the FPS counter is shown in the viewport.</summary>
            public int DisplayFps { get; set; } = 1;
            /// <summary>Gets or sets the <see cref="QuickPreviewFlags"/> bitmask for quick file preview behavior.</summary>
            public int QuickFilePreview { get; set; }
            /// <summary>Gets or sets whether the file explorer panel is opened automatically on start (suppressed on first startup or when command-line files are provided).</summary>
            public int OpenExplorerOnStart { get; set; } = 1;
            /// <summary>Gets or sets the font size used in the text viewer.</summary>
            public int TextViewerFontSize { get; set => field = Math.Clamp(value, 8, 24); } = 10;
            /// <summary>Gets or sets whether the package file list uses grid view (1) or list view (0).</summary>
            public int PackageGridView { get; set; } = 1;
            /// <summary>Gets or sets the grid thumbnail size index (0-4, mapping to <see cref="GUI.Types.PackageViewer.ThumbnailRenderers.ThumbnailSizes"/> enum).</summary>
            public int PackageGridSize { get; set => field = Math.Clamp(value, 0, Enum.GetValues<Types.PackageViewer.ThumbnailRenderers.ThumbnailSizes>().Length - 1); } = 2;
            /// <summary>Internal settings file version used to apply migrations when upgrading from older versions. Do not modify manually.</summary>
            public int _VERSION_DO_NOT_MODIFY { get; set; }
            /// <summary>Gets or sets the application update check state.</summary>
            public AppUpdateState Update { get; set; } = new();
        }

        /// <summary>Gets whether this is the first time the application has been launched (no prior settings were found).</summary>
        public static bool IsFirstStartup { get; private set; }
        /// <summary>Gets the folder path where the settings file and other persistent application data are stored.</summary>
        public static string SettingsFolder { get; private set; } = string.Empty;
        private static string SettingsFilePath = string.Empty;
        private static bool SaveDisabled;

        /// <summary>Gets the active application configuration.</summary>
        public static AppConfig Config { get; private set; } = new AppConfig();

        /// <summary>Raised when <see cref="AppConfig.SavedCameras"/> is mutated, signaling subscribers to refresh their camera lists.</summary>
        public static event EventHandler? RefreshCamerasOnSave;
        /// <summary>Raises the <see cref="RefreshCamerasOnSave"/> event.</summary>
        public static void InvokeRefreshCamerasOnSave() => RefreshCamerasOnSave?.Invoke(null, EventArgs.Empty);

        /// <summary>
        /// Loads the application configuration from disk, applies migrations for older
        /// settings file versions, validates the values, and populates <see cref="Config"/>.
        /// </summary>
        public static void Load()
        {
            SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Viewer");
            SettingsFilePath = Path.Combine(SettingsFolder, "settings.vdf");

            Directory.CreateDirectory(SettingsFolder);

            SaveDisabled = Automation.Automation.IsEnabled;

            byte[]? settingsData = null;

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    using var stream = File.Open(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    settingsData = new byte[stream.Length];
                    stream.ReadExactly(settingsData);
                }
                else
                {
                    IsFirstStartup = true;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The file may only be locked by another process, so it must not be overwritten with defaults
                Log.Error(nameof(Settings), $"Failed to read '{SettingsFilePath}', settings will not be saved.{Environment.NewLine}{e}");
                SaveDisabled = true;
            }

            try
            {
                if (settingsData != null)
                {
                    using var stream = new MemoryStream(settingsData);
                    Config = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize<AppConfig>(stream, KVSerializerOptions.DefaultOptions);
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(Settings), $"Failed to parse '{SettingsFilePath}', is it corrupted?{Environment.NewLine}{e}");
                IsFirstStartup = true;

                if (!SaveDisabled)
                {
                    try
                    {
                        var corruptedPath = Path.ChangeExtension(SettingsFilePath, $".corrupted-{DateTimeOffset.Now.ToUnixTimeSeconds()}.txt");
                        File.Move(SettingsFilePath, corruptedPath);

                        Log.Error(nameof(Settings), $"Corrupted '{Path.GetFileName(SettingsFilePath)}' has been renamed to '{Path.GetFileName(corruptedPath)}'.");

                        Save();
                    }
                    catch
                    {
                        //
                    }
                }
            }

            Migrate(Config._VERSION_DO_NOT_MODIFY);

            if (string.IsNullOrEmpty(Config.OpenDirectory))
            {
                var steamPath = Path.Join(GameFolderLocator.SteamPath, "steamapps", "common");

                if (Directory.Exists(steamPath))
                {
                    Config.OpenDirectory = steamPath;
                }
            }

            // If the version changed, force an update check (if enabled)
            if (Config.Update.Version != Program.ProductVersion)
            {
                Config.Update.Version = Program.ProductVersion;
                Config.Update.NextCheck = string.Empty;

                // Installing a build, whether through the updater or by hand, is choosing its channel
                Config.Update.Channel = Program.BuildChannel;
            }

            Config._VERSION_DO_NOT_MODIFY = SettingsFileCurrentVersion;
        }

        /// <summary>
        /// Converts values whose meaning changed since the settings file version <paramref name="fileVersion"/> was written.
        /// </summary>
        /// <param name="fileVersion">The settings file version read from disk, 0 when no file was loaded.</param>
        private static void Migrate(int fileVersion)
        {
            if (fileVersion <= 0 || fileVersion == SettingsFileCurrentVersion)
            {
                return;
            }

            Log.Info(nameof(Settings), $"Settings version changed: {fileVersion} -> {SettingsFileCurrentVersion}");

            // version 16: field of view changed from vertical to horizontal at 4:3
            if (fileVersion < 16)
            {
                var oldVerticalRadians = float.DegreesToRadians(Config.FieldOfView);
                var horizontalAt4By3Radians = 2f * MathF.Atan(MathF.Tan(oldVerticalRadians * 0.5f) * (4f / 3f));
                Config.FieldOfView = float.RadiansToDegrees(horizontalAt4By3Radians);
            }
        }

        /// <summary>
        /// Serializes the current <see cref="Config"/> to disk, writing atomically via a temp file.
        /// A run driven by automation, or one that could not read the file, keeps every change in memory instead.
        /// </summary>
        public static void Save()
        {
            if (SaveDisabled)
            {
                return;
            }

            // Written next to the settings file so the move replaces it in one step
            var tempFile = Path.Join(SettingsFolder, Path.GetRandomFileName());

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, Config, nameof(ValveResourceFormat));
                }

                File.Move(tempFile, SettingsFilePath, overwrite: true);
            }
            catch
            {
                File.Delete(tempFile);
                throw;
            }
        }

        /// <summary>
        /// Appends <paramref name="path"/> to the end of the recent files list (most recent last),
        /// removing any duplicate entry and trimming the oldest entries to <see cref="RecentFilesLimit"/>.
        /// The caller is responsible for calling <see cref="Save"/>.
        /// </summary>
        /// <param name="path">The absolute file path to record as recently opened.</param>
        public static void TrackRecentFile(string path)
        {
            Config.RecentFiles.Remove(path);
            Config.RecentFiles.Add(path);

            if (Config.RecentFiles.Count > RecentFilesLimit)
            {
                Config.RecentFiles.RemoveRange(0, Config.RecentFiles.Count - RecentFilesLimit);
            }
        }

        /// <summary>
        /// Clears the recent files list and saves.
        /// </summary>
        public static void ClearRecentFiles()
        {
            Config.RecentFiles.Clear();
            Save();
        }
    }
}
