using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace GUI.Utils
{
    static class Settings
    {
        public static List<string> GameSearchPaths = new List<string>();

        public static Color BackgroundColor = Color.Black;

        public static void Load()
        {
            // TODO: Be dumb about it for now.
            if (!File.Exists("gamepaths.txt"))
            {
                return;
            }

            GameSearchPaths.AddRange(File.ReadAllLines("gamepaths.txt"));
        }

        public static void Save()
        {
            File.WriteAllLines("gamepaths.txt", GameSearchPaths);
        }
    }
}
