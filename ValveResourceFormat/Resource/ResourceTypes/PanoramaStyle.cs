using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class PanoramaStyle : Panorama
    {
        private BinaryKV3 SourceMap;

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            SourceMap = Resource.GetBlockByType(BlockType.SrMa) as BinaryKV3;
        }

        public override string ToString() => ToString(true);

        public string ToString(bool applySourceMapIfPresent)
        {
            if (applySourceMapIfPresent && SourceMap != default && SourceMap.Data.GetProperty<object>("DBITSLC") is not null)
            {
#if false
                var sourceBytes = PanoramaSourceMapDecoder.Decode(Data, SourceMap.AsKeyValueCollection());
                return Encoding.UTF8.GetString(sourceBytes);
#endif

                return ToStringPrettified(Data);
            }
            else
            {
                return base.ToString();
            }
        }

        private static string ToStringPrettified(byte[] data)
        {
            var input = Encoding.UTF8.GetString(data);
            using var output = new IndentedTextWriter();

            output.WriteLine($"/* Prettified by {StringToken.VRF_GENERATOR} */");
            output.WriteLine();

            var insideString = false;
            var quoteType = ' ';

            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];

                if (insideString)
                {
                    output.Write(c);

                    if (c == quoteType && (i == 0 || input[i - 1] != '\\'))
                    {
                        insideString = false;
                    }
                }
                else
                {
                    if ((c == '"' || c == '\'') && (i == 0 || input[i - 1] != '\\'))
                    {
                        quoteType = c;
                        insideString = true;
                        output.Write(c);
                    }
                    else if (c == ';')
                    {
                        output.Write(c);
                        output.WriteLine();
                    }
                    else if (c == '{')
                    {
                        output.WriteLine();
                        output.Write(c);
                        output.WriteLine();
                        output.Indent++;
                    }
                    else if (c == '}')
                    {
                        output.Indent--;
                        output.Write(c);
                        output.WriteLine();
                        output.WriteLine();
                    }
                    else
                    {
                        output.Write(c);
                    }
                }
            }

            return output.ToString();
        }
    }

#if false
    static class PanoramaSourceMapDecoder
    {
        public static byte[] Decode(byte[] data, KVObject sourceMap)
        {
            var mapping = sourceMap.GetArray("DBITSLC", kvArray => (kvArray.GetInt32Property("0"), kvArray.GetInt32Property("1"), kvArray.GetInt32Property("2")));

            var output = new List<IEnumerable<byte>>();

            var currentCol = 0;
            var currentLine = 1;

            for (var i = 0; i < mapping.Length - 1; i++)
            {
                var (startIndex, sourceLine, sourceColumn) = mapping[i];
                var (nextIndex, _, _) = mapping[i + 1];

                // Prepend newlines if they are in front of this chunk according to sourceLineByteIndices
                if (currentLine < sourceLine)
                {
                    output.Add(Enumerable.Repeat(Encoding.UTF8.GetBytes("\n")[0], sourceLine - currentLine));
                    currentCol = 0;
                    currentLine = sourceLine;
                }
                else if (sourceLine < currentLine)
                {
                    // Referring back to an object higher in hierarchy, also add newline here
                    output.Add(Enumerable.Repeat(Encoding.UTF8.GetBytes("\n")[0], 1));
                    currentCol = 0;
                    currentLine++;
                }

                // Prepend spaces until we catch up to the index we need to be at
                if (currentCol < sourceColumn)
                {
                    output.Add(Enumerable.Repeat(Encoding.UTF8.GetBytes(" ")[0], sourceColumn - currentCol));
                    currentCol = sourceColumn;
                }

                // Copy destination
                var length = nextIndex - startIndex;
                output.Add(data.Skip(startIndex).Take(length));
                currentCol += length;
            }

            output.Add(Enumerable.Repeat(Encoding.UTF8.GetBytes("\n")[0], 1));
            output.Add(data.Skip(mapping[^1].Item1));

            return output.SelectMany(_ => _).ToArray();
        }
    }
#endif
}
