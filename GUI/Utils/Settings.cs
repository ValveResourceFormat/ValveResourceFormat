using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using ValveKeyValue;

namespace GUI.Utils
{
    static class Settings
    {
        private const int SettingsFileCurrentVersion = 6;
        private const int RecentFilesLimit = 20;

        public class AppConfig
        {
            public List<string> GameSearchPaths { get; set; } = [];
            public string OpenDirectory { get; set; } = string.Empty;
            public string SaveDirectory { get; set; } = string.Empty;
            public List<string> BookmarkedFiles { get; set; } = [];
            public List<string> RecentFiles { get; set; } = new(RecentFilesLimit);
            public Dictionary<string, float[]> SavedCameras { get; set; } = [];
            public int MaxTextureSize { get; set; }
            public int FieldOfView { get; set; }
            public int AntiAliasingSamples { get; set; }
            public int WindowTop { get; set; }
            public int WindowLeft { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int WindowState { get; set; } = (int)FormWindowState.Normal;
            public float Volume { get; set; }
            public int Vsync { get; set; }
            public int DisplayFps { get; set; }
            public int _VERSION_DO_NOT_MODIFY { get; set; }
        }

        private static string SettingsFolder;
        private static string SettingsFilePath;

        public static AppConfig Config { get; set; } = new AppConfig();

        public static event EventHandler RefreshCamerasOnSave;
        public static void InvokeRefreshCamerasOnSave() => RefreshCamerasOnSave.Invoke(null, null);

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

            Config.SavedCameras ??= [];
            Config.BookmarkedFiles ??= [];
            Config.RecentFiles ??= new(RecentFilesLimit);

            if (string.IsNullOrEmpty(Config.OpenDirectory) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Config.OpenDirectory = Path.Join(GetSteamPath(), "steamapps", "common");
            }

            if (Config.MaxTextureSize <= 0)
            {
                Config.MaxTextureSize = 1024;
            }
            else if (Config.MaxTextureSize > 10240)
            {
                Config.MaxTextureSize = 10240;
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

            if (Config._VERSION_DO_NOT_MODIFY < 2) // version 2: added anti aliasing samples
            {
                Config.AntiAliasingSamples = 8;
            }

            if (Config._VERSION_DO_NOT_MODIFY < 3) // version 3: added volume
            {
                Config.Volume = 0.5f;
            }

            if (Config._VERSION_DO_NOT_MODIFY < 4)
            {
                Config.Vsync = 1;
            }

            if (Config._VERSION_DO_NOT_MODIFY < 5)
            {
                Config.DisplayFps = 1;
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

        public static string GetSteamPath()
        {
            try
            {
                using var key =
                    Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                    Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam");

                if (key?.GetValue("SteamPath") is string steamPath)
                {
                    return Path.GetFullPath(steamPath);
                }
            }
            catch
            {
                // Don't care about registry exceptions
            }

            return string.Empty;
        }
    }
}
