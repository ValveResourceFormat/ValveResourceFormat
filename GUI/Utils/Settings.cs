using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;

namespace GUI.Utils
{
    /// <summary>
    /// Manages application settings.
    /// </summary>
    static class Settings
    {
        private const int SettingsFileCurrentVersion = 12;
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
        /// Holds state related to automatic application update checks.
        /// </summary>
        public class AppUpdateState
        {
            /// <summary>Gets or sets whether to automatically check for updates on startup.</summary>
            public bool CheckAutomatically { get; set; }
            /// <summary>Gets or sets whether a newer version of the application is available.</summary>
            public bool UpdateAvailable { get; set; }
            /// <summary>Gets or sets the timestamp of the last update check.</summary>
            public string LastCheck { get; set; } = string.Empty;
            /// <summary>Gets or sets the application version recorded the last time settings were loaded, used to detect version changes and reset update state.</summary>
            public string Version { get; set; } = string.Empty;
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
            public int MaxTextureSize { get; set; }
            /// <summary>Gets or sets the shadow map resolution.</summary>
            public int ShadowResolution { get; set; }
            /// <summary>Gets or sets the camera field of view in degrees.</summary>
            public float FieldOfView { get; set; }
            /// <summary>Gets or sets the number of MSAA samples used for anti-aliasing.</summary>
            public int AntiAliasingSamples { get; set; }
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
            public float Volume { get; set; }
            /// <summary>Gets or sets the swap interval (the number of screen updates to wait between swapping front and back buffers).</summary>
            public int Vsync { get; set; }
            /// <summary>Gets or sets whether the FPS counter is shown in the viewport.</summary>
            public int DisplayFps { get; set; }
            /// <summary>Gets or sets the <see cref="QuickPreviewFlags"/> bitmask for quick file preview behavior.</summary>
            public int QuickFilePreview { get; set; }
            /// <summary>Gets or sets whether the file explorer panel is opened automatically on start (suppressed on first startup or when command-line files are provided).</summary>
            public int OpenExplorerOnStart { get; set; }
            /// <summary>Gets or sets the font size used in the text viewer.</summary>
            public int TextViewerFontSize { get; set; }
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

        /// <summary>Gets or sets the active application configuration.</summary>
        public static AppConfig Config { get; private set; } = new AppConfig();

        /// <summary>Raised when <see cref="AppConfig.SavedCameras"/> is mutated, signaling subscribers to refresh their camera lists.</summary>
        public static event EventHandler? RefreshCamerasOnSave;
        /// <summary>Raises the <see cref="RefreshCamerasOnSave"/> event.</summary>
        public static void InvokeRefreshCamerasOnSave() => RefreshCamerasOnSave?.Invoke(null, EventArgs.Empty);

        /// <summary>
        /// Loads the application configuration from disk, applies defaults and migrations for older
        /// settings file versions, and populates <see cref="Config"/>.
        /// </summary>
        public static void Load()
        {
            SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Viewer");
            SettingsFilePath = Path.Combine(SettingsFolder, "settings.vdf");

            Directory.CreateDirectory(SettingsFolder);

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    using var stream = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read);
                    Config = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize<AppConfig>(stream, KVSerializerOptions.DefaultOptions);
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(Settings), $"Failed to parse '{SettingsFilePath}', is it corrupted?{Environment.NewLine}{e}");

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

            var currentVersion = Config._VERSION_DO_NOT_MODIFY;

            if (currentVersion > SettingsFileCurrentVersion)
            {
                var result = MessageBox.Show(
                    $"Your current settings.vdf has a higher version ({currentVersion}) than currently supported ({SettingsFileCurrentVersion}). You likely ran an older version of Source 2 Viewer and your settings may get reset.\n\nDo you want to continue?",
                    "Source 2 Viewer downgraded",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result != DialogResult.Yes)
                {
                    Environment.Exit(1);
                    return;
                }
            }

            Config.GameSearchPaths ??= [];
            Config.SavedCameras ??= [];
            Config.BookmarkedFiles ??= [];
            Config.RecentFiles ??= new(RecentFilesLimit);
            Config.Update ??= new();

            if (string.IsNullOrEmpty(Config.OpenDirectory))
            {
                var steamPath = Path.Join(GameFolderLocator.SteamPath, "steamapps", "common");

                if (Directory.Exists(steamPath))
                {
                    Config.OpenDirectory = steamPath;
                }
            }

            if (Config.MaxTextureSize <= 0)
            {
                Config.MaxTextureSize = 1024;
            }
            else if (Config.MaxTextureSize > 10240)
            {
                Config.MaxTextureSize = 10240;
            }

            if (Config.ShadowResolution <= 0)
            {
                Config.ShadowResolution = 2048;
            }
            else if (Config.ShadowResolution > 4096)
            {
                Config.ShadowResolution = 4096;
            }

            if (Config.FieldOfView <= 0)
            {
                Config.FieldOfView = 60;
            }
            else if (Config.FieldOfView > 170)
            {
                Config.FieldOfView = 170;
            }

            Config.AntiAliasingSamples = Math.Clamp(Config.AntiAliasingSamples, 0, 64);
            Config.Volume = MathUtils.Saturate(Config.Volume);
            Config.TextViewerFontSize = Math.Clamp(Config.TextViewerFontSize, 8, 24);

            if (currentVersion < 2) // version 2: added anti aliasing samples
            {
                Config.AntiAliasingSamples = 8;
            }

            if (currentVersion < 3) // version 3: added volume
            {
                Config.Volume = 0.5f;
            }

            if (currentVersion < 4) // version 4: added vsync
            {
                Config.Vsync = 1;
            }

            if (currentVersion < 5) // version 5: added display fps
            {
                Config.DisplayFps = 1;
            }

            if (currentVersion < 8) // version 8: added explorer on start
            {
                Config.OpenExplorerOnStart = 1;
            }

            if (currentVersion < 10) // version 10: added startup window
            {
                IsFirstStartup = true;
            }

            if (currentVersion < 11) // version 11: added text viewer font size
            {
                Config.TextViewerFontSize = 10;
            }

            if (currentVersion < 12) // version 12: enable automatic update checks by default
            {
                Config.Update.CheckAutomatically = true;
            }

            if (currentVersion > 0 && currentVersion != SettingsFileCurrentVersion)
            {
                Log.Info(nameof(Settings), $"Settings version changed: {currentVersion} -> {SettingsFileCurrentVersion}");
            }

            // If the version changed, force an update check (if enabled)
            if (Config.Update.Version != Application.ProductVersion)
            {
                Config.Update.Version = Application.ProductVersion;
                Config.Update.UpdateAvailable = false;
                Config.Update.LastCheck = string.Empty;
            }

            Config._VERSION_DO_NOT_MODIFY = SettingsFileCurrentVersion;
        }

        /// <summary>
        /// Serializes the current <see cref="Config"/> to disk, writing atomically via a temp file.
        /// </summary>
        public static void Save()
        {
            var tempFile = Path.GetTempFileName();

            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, Config, nameof(ValveResourceFormat));
            }

            File.Move(tempFile, SettingsFilePath, overwrite: true);
        }

        /// <summary>
        /// Adds <paramref name="path"/> to the top of the recent files list, removing any duplicate
        /// entry, trimming the list to <see cref="RecentFilesLimit"/>, then saves.
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

            Save();
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
