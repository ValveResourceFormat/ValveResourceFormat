using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Theme
{
    public sealed class ThemeCustom : IThemeData
    {
        public string Name { get; }
        public Color Primary { get; }

        public Color Secondary { get; }

        public Color Tertiary { get; }

        public Color Quaternary { get; }

        public Color Quinary { get; }

        public Color Senary { get; }

        public ThemeCustom(string name, Color primary, Color secondary, Color tertiary, Color quaternary, Color quinary, Color senary)
        {
            Name = name;
            Primary = primary;
            Secondary = secondary;
            Tertiary = tertiary;
            Quaternary = quaternary;
            Quinary = quinary;
            Senary = senary;
        }
    }
}
