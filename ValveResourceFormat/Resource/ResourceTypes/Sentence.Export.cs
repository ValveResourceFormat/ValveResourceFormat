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
            using var writer = new IndentedTextWriter();
            writer.NewLine = "\n";

            writer.WriteLine("VERSION 1.0");
            writer.WriteLine("PLAINTEXT");
            writer.WriteLine("{");
            writer.WriteLine("}");
            writer.WriteLine("WORDS");
            writer.WriteLine("{");

            if (RunTimePhonemes.Length > 0)
            {
                writer.Indent++;

                var word = new StringBuilder(RunTimePhonemes.Length);
                foreach (var phoneme in RunTimePhonemes)
                {
                    word.Append((char)phoneme.PhonemeCode);
                }

                var start = RunTimePhonemes[0].StartTime;
                var end = RunTimePhonemes[^1].EndTime;

                writer.WriteLine("WORD {0} {1:0.000} {2:0.000}", word, start, end);
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var phoneme in RunTimePhonemes)
                {
                    var symbol = (char)phoneme.PhonemeCode;
                    writer.WriteLine("{0} {1} {2:0.000} {3:0.000} 1", phoneme.PhonemeCode, symbol, phoneme.StartTime, phoneme.EndTime);
                }

                writer.Indent--;
                writer.WriteLine("}");
                writer.Indent--;
            }

            writer.WriteLine("}");
            writer.WriteLine("EMPHASIS");
            writer.WriteLine("{");
            writer.WriteLine("}");
            writer.WriteLine("OPTIONS");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("voice_duck 0");
            writer.Indent--;
            writer.WriteLine("}");

            return writer.ToString();
        }
    }
}
