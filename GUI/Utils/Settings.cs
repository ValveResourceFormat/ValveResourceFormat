using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ValveKeyValue;
using ValveResourceFormat.IO;

#nullable disable

#pragma warning disable WFO5001
namespace GUI.Utils
{
    static class Settings
    {
        private const int SettingsFileCurrentVersion = 11;
        private const int RecentFilesLimit = 20;

        public enum AppTheme : int
        {
            System = 0,
            Light = 1,
            Dark = 2,
        }

        [Flags]
        public enum QuickPreviewFlags : int
        {
            Enabled = 1 << 0,
            AutoPlaySounds = 1 << 1,
        }

        public class AppUpdateState
        {
            public bool CheckAutomatically { get; set; }
            public bool UpdateAvailable { get; set; }
            public string LastCheck { get; set; }
            public string Version { get; set; }
        }

        public class AppConfig
        {
            public List<string> GameSearchPaths { get; set; }
            public string OpenDirectory { get; set; } = string.Empty;
            public string SaveDirectory { get; set; } = string.Empty;
            public List<string> BookmarkedFiles { get; set; }
            public List<string> RecentFiles { get; set; }
            public Dictionary<string, float[]> SavedCameras { get; set; }
            public int Theme { get; set; }
            public int MaxTextureSize { get; set; }
            public int ShadowResolution { get; set; }
            public float FieldOfView { get; set; }
            public int AntiAliasingSamples { get; set; }
            public int WindowTop { get; set; }
            public int WindowLeft { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int WindowState { get; set; }
            public float Volume { get; set; }
            public int Vsync { get; set; }
            public int DisplayFps { get; set; }
            public int QuickFilePreview { get; set; }
            public int OpenExplorerOnStart { get; set; }
            public int TextViewerFontSize { get; set; }
            public int _VERSION_DO_NOT_MODIFY { get; set; }
            public AppUpdateState Update { get; set; }
        }

        public static bool IsFirstStartup { get; private set; }
        public static string SettingsFolder { get; private set; }
        private static string SettingsFilePath;

        public static AppConfig Config { get; set; } = new AppConfig();

        public static event EventHandler RefreshCamerasOnSave;
        public static void InvokeRefreshCamerasOnSave() => RefreshCamerasOnSave.Invoke(null, null);

        public static string GpuRendererAndDriver;

        public static void Load()
        {
            SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Viewer");
            SettingsFilePath = Path.Combine(SettingsFolder, "settings.vdf");

            Directory.CreateDirectory(SettingsFolder);

            // Before 2023-09-08, settings were saved next to the executable
            var legacySettings = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName), "settings.txt");

            if (File.Exists(legacySettings) && !File.Exists(SettingsFilePath))
            {
                Log.Info(nameof(Settings), $"Moving '{legacySettings}' to '{SettingsFilePath}'.");

                File.Move(legacySettings, SettingsFilePath);
            }

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
            else if (Config.FieldOfView >= 120)
            {
                Config.FieldOfView = 120;
            }

            if (Config.FieldOfView <= 0)
            {
                Config.FieldOfView = 60;
            }
            else if (Config.FieldOfView >= 120)
            {
                Config.FieldOfView = 120;
            }

            Config.AntiAliasingSamples = Math.Clamp(Config.AntiAliasingSamples, 0, 64);
            Config.Volume = Math.Clamp(Config.Volume, 0f, 1f);
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

        public static void Save()
        {
            var tempFile = Path.GetTempFileName();

            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, Config, nameof(ValveResourceFormat));
            }

            File.Move(tempFile, SettingsFilePath, overwrite: true);
        }

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

        public static void ClearRecentFiles()
        {
            Config.RecentFiles.Clear();
            Save();
        }

        public static SystemColorMode GetSystemColor() =>
            (AppTheme)Config.Theme switch
            {
                AppTheme.Light => SystemColorMode.Classic,
                AppTheme.Dark => SystemColorMode.Dark,
                _ => SystemColorMode.System,
            };
    }
}
