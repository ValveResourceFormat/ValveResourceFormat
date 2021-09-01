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
    internal static class Settings
    {
        public class AppConfig
        {
            public List<string> GameSearchPaths { get; set; } = new List<string>();
            public string BackgroundColor { get; set; } = string.Empty;
            public string OpenDirectory { get; set; } = string.Empty;
            public string SaveDirectory { get; set; } = string.Empty;
            public Dictionary<string, float[]> SavedCameras { get; set; } = new Dictionary<string, float[]>();
            public int WindowTop { get; set; }
            public int WindowLeft { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int WindowState { get; set; } = (int)FormWindowState.Normal;
        }

        private static string SettingsFilePath;

        public static AppConfig Config { get; set; } = new AppConfig();

        public static Color BackgroundColor { get; set; } = Color.FromArgb(60, 60, 60);

        public static void Load()
        {
            SettingsFilePath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName), "settings.txt");

            if (!File.Exists(SettingsFilePath))
            {
                Save();
                return;
            }

            using (var stream = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read))
            {
                Config = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize<AppConfig>(stream, KVSerializerOptions.DefaultOptions);
            }

            BackgroundColor = ColorTranslator.FromHtml(Config.BackgroundColor);

            if (Config.SavedCameras == null)
            {
                Config.SavedCameras = new Dictionary<string, float[]>();
            }

            if (string.IsNullOrEmpty(Config.OpenDirectory) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Config.OpenDirectory = GetSteamPath();
            }
        }

        public static void Save()
        {
            Config.BackgroundColor = ColorTranslator.ToHtml(BackgroundColor);

            using (var stream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, Config, nameof(ValveResourceFormat));
            }
        }

        private static string GetSteamPath()
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                          RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                              .OpenSubKey("SOFTWARE\\Valve\\Steam");

                if (key != null && key.GetValue("SteamPath") is string steamPath)
                {
                    return Path.GetFullPath(Path.Combine(steamPath, "steamapps", "common"));
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
