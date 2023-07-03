using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using ValveKeyValue;

namespace GUI.Utils
{
    static class Settings
    {
        private const int RecentFilesLimit = 20;

        public class AppConfig
        {
            public List<string> GameSearchPaths { get; set; } = new();
            public string BackgroundColor { get; set; } = string.Empty;
            public string OpenDirectory { get; set; } = string.Empty;
            public string SaveDirectory { get; set; } = string.Empty;
            public List<string> RecentFiles { get; set; } = new(RecentFilesLimit);
            public Dictionary<string, float[]> SavedCameras { get; set; } = new();
            public int MaxTextureSize { get; set; }
            public int FieldOfView { get; set; }
            public int WindowTop { get; set; }
            public int WindowLeft { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int WindowState { get; set; } = (int)FormWindowState.Normal;
        }

        private static string SettingsFilePath;

        public static AppConfig Config { get; set; } = new AppConfig();

        public static Color BackgroundColor { get; set; }

        public static event EventHandler RefreshCamerasOnSave;
        public static void InvokeRefreshCamerasOnSave() => RefreshCamerasOnSave.Invoke(null, null);

        public static void Load()
        {
            SettingsFilePath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName), "settings.txt");

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
                Console.WriteLine($"Failed to parse '{SettingsFilePath}', is it corrupted?");
                Console.WriteLine(e);

                try
                {
                    var corruptedPath = Path.ChangeExtension(SettingsFilePath, $".corrupted-{DateTimeOffset.Now.ToUnixTimeSeconds()}.txt");
                    File.Move(SettingsFilePath, corruptedPath);

                    Console.WriteLine($"Corrupted '{Path.GetFileName(SettingsFilePath)}' has been renamed to '{Path.GetFileName(corruptedPath)}'.");

                    Save();
                }
                catch
                {
                    //
                }
            }

            try
            {
                BackgroundColor = ColorTranslator.FromHtml(Config.BackgroundColor);
            }
            catch
            {
                //
            }

            if (BackgroundColor.IsEmpty)
            {
                BackgroundColor = Color.FromArgb(60, 60, 60);
            }

            Config.SavedCameras ??= new();
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
                Config.FieldOfView = 50;
            }
            else if (Config.FieldOfView >= 120)
            {
                Config.FieldOfView = 120;
            }
        }

        public static void Save()
        {
            Config.BackgroundColor = ColorTranslator.ToHtml(BackgroundColor);

            using var stream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, Config, nameof(ValveResourceFormat));
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
