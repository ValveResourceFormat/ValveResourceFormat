using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Theme
{
    public sealed class ThemeWhite : IThemeData
    {
        public string Name => "White";
        public Color Primary { get; }

        public Color Secondary { get; }

        public Color Tertiary { get; }

        public Color Quaternary { get; }

        public Color Quinary { get; }

        public Color Senary { get; }

        public ThemeWhite()
        {
            Primary = SystemColors.Control;
            Secondary = SystemColors.ControlDark;
            Tertiary = SystemColors.ControlText;
            Quaternary = SystemColors.ActiveBorder;
            Quinary = SystemColors.ControlLight;
            Senary = SystemColors.ControlLightLight;
        }
    }
}
