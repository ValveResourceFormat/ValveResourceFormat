using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class PanoramaStyle : Panorama
    {
        private BinaryKV3 SourceMap;

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            SourceMap = resource.GetBlockByType(BlockType.SrMa) as BinaryKV3;
        }

        public override string ToString() => ToString(true);

        public string ToString(bool applySourceMapIfPresent)
        {
            if (applySourceMapIfPresent && SourceMap != default && SourceMap.Data.GetProperty<object>("DBITSLC") is not null)
            {
                var sourceBytes = PanoramaSourceMapDecoder.Decode(Data, SourceMap.AsKeyValueCollection());
                return Encoding.UTF8.GetString(sourceBytes);
            }
            else
            {
                return base.ToString();
            }
        }
    }

    static class PanoramaSourceMapDecoder
    {
        public static byte[] Decode(byte[] data, IKeyValueCollection sourceMap)
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
            output.Add(data.Skip(mapping[mapping.Length - 1].Item1));

            return output.SelectMany(_ => _).ToArray();
        }
    }
}
