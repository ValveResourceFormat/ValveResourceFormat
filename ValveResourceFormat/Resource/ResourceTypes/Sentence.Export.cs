using System.Globalization;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class Sentence
    {
        /// <summary>
        /// Serializes the sentence to the plaintext phoneme format used by Source's
        /// <c>resourcecompiler -extract_sentence</c>. Placing the resulting <c>.txt</c> next to a
        /// source sound recompiles the phoneme data back into the <c>vsnd</c>.
        /// </summary>
        /// <remarks>
        /// A compiled sound only stores the flat runtime phoneme stream, so the original word
        /// grouping, plaintext and emphasis samples cannot be recovered. All phonemes are emitted
        /// into a single <c>WORD</c> block, which the compiler flattens back to the same stream.
        /// A phoneme code is the unicode code point of its IPA symbol; the compiler keys off the
        /// numeric code, so the symbol is written purely for readability.
        /// </remarks>
        public string ToValveSentence()
        {
            var sb = new StringBuilder();

            sb.Append("VERSION 1.0\n");
            sb.Append("PLAINTEXT\n{\n}\n");
            sb.Append("WORDS\n{\n");

            if (RunTimePhonemes.Length > 0)
            {
                var word = new StringBuilder(RunTimePhonemes.Length);
                foreach (var phoneme in RunTimePhonemes)
                {
                    word.Append((char)phoneme.PhonemeCode);
                }

                var start = RunTimePhonemes[0].StartTime;
                var end = RunTimePhonemes[^1].EndTime;

                sb.Append("\tWORD ").Append(word).Append(CultureInfo.InvariantCulture, $" {start:0.000} {end:0.000}\n\t{{\n");

                foreach (var phoneme in RunTimePhonemes)
                {
                    var symbol = (char)phoneme.PhonemeCode;
                    sb.Append(CultureInfo.InvariantCulture, $"\t\t{phoneme.PhonemeCode} {symbol} {phoneme.StartTime:0.000} {phoneme.EndTime:0.000} 1\n");
                }

                sb.Append("\t}\n");
            }

            sb.Append("}\n");
            sb.Append("EMPHASIS\n{\n}\n");
            sb.Append("OPTIONS\n{\n\tvoice_duck 0\n}\n");

            return sb.ToString();
        }
    }
}
