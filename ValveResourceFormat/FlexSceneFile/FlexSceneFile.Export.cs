using System.Globalization;
using System.Text;

#nullable disable

namespace ValveResourceFormat.FlexSceneFile
{
    partial class FlexSceneFile
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("$keys ");
            sb.AppendJoin(' ', KeyNames);
            sb.AppendLine();
            sb.AppendLine("$hasweighting");

            for (var i = 0; i < FlexSettings.Length; i++)
            {
                PrintFlexWeights(sb, FlexSettings[i]);
            }

            return sb.ToString();
        }

        private void PrintFlexWeights(StringBuilder sb, FlexSetting setting)
        {
            //Setting name
            sb.Append(CultureInfo.InvariantCulture, $"\"{setting.Name}\" ");
            var index = setting.Phoneme;

            //Phoneme code
            sb.Append('"');
            if (setting.Phoneme <= 'z')
            {
                sb.Append((char)setting.Phoneme);
            }
            else
            {
                var phonemeCode = setting.Phoneme.ToString("X4", CultureInfo.InvariantCulture);
                sb.Append("0x");
                sb.Append(phonemeCode.ToLowerInvariant());
            }
            sb.Append('"');
            sb.Append(' ');

            //Weights and influences
            for (var i = 0; i < KeyNames.Length; i++)
            {
                var weight = setting.GetWeight(i);
                sb.Append(weight.Weight.ToString("0.000#", CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(weight.Influence.ToString("0.000#", CultureInfo.InvariantCulture));
                sb.Append(' ');
            }

            //Description
            var description = PhonemeToDescription(setting.Phoneme);
            if (description == null)
            {
                if (setting.Name == "<sil>")
                {
                    description = "Silence";
                }
                else
                {
                    description = setting.Name;
                }
            }
            sb.Append('"');
            sb.Append(description);
            sb.Append('"');
            sb.AppendLine();
        }

        private static string PhonemeToDescription(int phoneme)
        {
            return phoneme switch
            {
                0x025a => "URn : rhotacized schwa",
                'm' => "Mat : voiced bilabial nasal",
                'p' => "Put; voiceless alveolar stop",
                'w' => "With : voiced labial-velar approximant",
                'f' => "Fork : voiceless labiodental fricative",
                'v' => "Val : voiced labialdental fricative",
                0x0279 => "Red : voiced alveolar approximant",
                'r' => "Red : voiced alveolar trill",
                0x027b => "Red : voiced retroflex approximant",
                0x025d => "URn : rhotacized lower-mid central vowel",
                0x00f0 => "THen : voiced dental fricative",
                0x03b8 => "THin : voiceless dental fricative",
                0x0283 => "SHe : voiceless postalveolar fricative",
                0x02a4 => "Joy : voiced postalveolar afficate",
                0x02a7 => "CHin : voiceless postalveolar affricate",
                's' => "Sit : voiceless alveolar fricative",
                'z' => "Zap : voiced alveolar fricative",
                'd' => "Dig : voiced bilabial stop",
                0x027e => "Dig : voiced alveolar flap or tap",
                'l' => "Lid : voiced alveolar lateral approximant",
                0x026b => "Lid : velarized voiced alveolar lateral approximant",
                'n' => "No : voiced alveolar nasal",
                't' => "Talk : voiceless bilabial stop",
                'o' => "gO : upper-mid back rounded vowel",
                'u' => "tOO : high back rounded vowel",
                'e' => "Ate : upper-mid front unrounded vowel",
                0x00e6 => "cAt : semi-low front unrounded vowel",
                0x0251 => "fAther : low back unrounded vowel",
                'a' => "fAther : low front unrounded vowel",
                'i' => "fEEl : high front unrounded vowel",
                'j' => "Yacht : voiced palatal approximant",
                0x028c => "cUt : lower-mid back unrounded vowel",
                0x0254 => "dOg : lower-mid back rounded vowel",
                0x0259 => "Ago : mid-central unrounded vowel",
                0x025c => "Ago : lower-mid central unrounded vowel",
                0x025b => "pEt : lower-mid front unrounded vowel",
                0x026a => "fIll : semi-high front unrounded vowel",
                0x0268 => "fIll : high central unrounded vowel",
                0x028a => "bOOk : semi-high back rounded vowel",
                'g' => "taG : voiced velar stop",
                0x0261 => "taG : voiced velar stop",
                'h' => "Help : voiceless glottal fricative",
                0x0266 => "Help : breathy-voiced glottal fricative",
                'k' => "Cut : voiceless velar stop",
                0x014b => "siNG : voiced velar nasal",
                0x0292 => "aZure : voiced postalveolar fricative",
                'b' => "Big : voiced alveolar stop",
                //'_' => "Silence",
                _ => null,
            };
        }
    }
}
