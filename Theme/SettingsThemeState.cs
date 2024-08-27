using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Theme
{
    public class SettingsThemeState
    {
        public SettingsThemeState() { }

        public string Name { get; set; }
        public int PrimaryR { get; set; }
        public int PrimaryG { get; set; }
        public int PrimaryB { get; set; }

        public int SecondaryR { get; set; }
        public int SecondaryG { get; set; }
        public int SecondaryB { get; set; }

        public int TertiaryR { get; set; }
        public int TertiaryG { get; set; }
        public int TertiaryB { get; set; }

        public int QuaternaryR { get; set; }
        public int QuaternaryG { get; set; }
        public int QuaternaryB { get; set; }

        public int QuinaryR { get; set; }
        public int QuinaryG { get; set; }
        public int QuinaryB { get; set; }

        public int SenaryR { get; set; }
        public int SenaryG { get; set; }
        public int SenaryB { get; set; }

        public IThemeData ToThemeData()
        {
            return new ThemeCustom(Name, FromByte(PrimaryR, PrimaryG, PrimaryB), FromByte(SecondaryR, SecondaryG, SecondaryB), FromByte(TertiaryR, TertiaryG, TertiaryB), FromByte(QuaternaryR, QuaternaryG, QuaternaryB), FromByte(QuinaryR, QuinaryG, QuinaryB), FromByte(SenaryR, SenaryG, SenaryB));
        }

        private static byte[] FromColor(Color c)
        {
            return new byte[] { c.R, c.G, c.B };
        }

        private static Color FromByte(int r, int g, int b)
        {
            return Color.FromArgb(255, r, g, b);
        }

        public static SettingsThemeState FromThemeData(IThemeData data)
        {
            byte[] primary = FromColor(data.Primary);
            byte[] secondary = FromColor(data.Secondary);
            byte[] tertiary = FromColor(data.Tertiary);
            byte[] quaternary = FromColor(data.Quaternary);
            byte[] quinary = FromColor(data.Quinary);
            byte[] senary = FromColor(data.Senary);

            return new SettingsThemeState()
            {
                Name = data.Name,

                PrimaryR = primary[0],
                PrimaryG = primary[1],
                PrimaryB = primary[2],

                SecondaryR = secondary[0],
                SecondaryG = secondary[1],
                SecondaryB = secondary[2],

                TertiaryR = tertiary[0],
                TertiaryG = tertiary[1],
                TertiaryB = tertiary[2],

                QuaternaryR = quaternary[0],
                QuaternaryG = quaternary[1],
                QuaternaryB = quaternary[2],

                QuinaryR = quinary[0],
                QuinaryG = quinary[1],
                QuinaryB = quinary[2],

                SenaryR = senary[0],
                SenaryG = senary[1],
                SenaryB = senary[2],
            };
        }
    }
}
