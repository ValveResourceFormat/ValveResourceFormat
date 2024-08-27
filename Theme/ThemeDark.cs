using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Theme
{
    public sealed class ThemeDark : IThemeData
    {
        public string Name => "Dark";
        public Color Primary { get; }

        public Color Secondary { get; }

        public Color Tertiary { get; }

        public Color Quaternary { get; }

        public Color Quinary { get; }

        public Color Senary { get; }

        public ThemeDark()
        {
            Primary = Color.FromArgb(255, 54, 54, 54); // Same as Hammer Editor 2 (QT);
            Secondary = Color.FromArgb(255, 38, 38, 38); // Same as Hammer Editor 2 (QT);
            Tertiary = Color.GhostWhite;
            Quaternary = Color.FromArgb(255, 139, 155, 153); // Same as Hammer Editor 2 (QT);
            Quinary = Color.FromArgb(255, 85, 94, 113); // Same as Hammer Editor 2 (QT);
            Senary = Color.FromArgb(255, 72, 80, 97); // Same as Hammer Editor 2 (QT);
        }
    }
}
