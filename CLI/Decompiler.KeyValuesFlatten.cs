using System.IO;
using System.Text;

namespace CLI
{
    public partial class Decompiler
    {
        /// <summary>
        /// Prints a KeyValues text file flattened to stdout, reading stdin when <paramref name="path"/> is null.
        /// </summary>
        private static int FlattenKeyValues(string? path)
        {
            byte[] data;

            if (path == null)
            {
                using var stdin = Console.OpenStandardInput();
                using var buffer = new MemoryStream();
                stdin.CopyTo(buffer);
                data = buffer.ToArray();
            }
            else if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
            }
            else
            {
                Console.Error.WriteLine($"Input \"{path}\" is not a file.");
                return 1;
            }

            // Line endings are kept the same on every platform, so outputs can be compared across them
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 16)
            {
                NewLine = "\n",
            };

            try
            {
                KeyValuesFlattener.Flatten(data, stdout);
            }
            catch (Exception e) when (KeyValuesFlattener.IsParseException(e))
            {
                Console.Error.WriteLine($"Failed to parse \"{path ?? "stdin"}\": {e.Message}");
                return 2;
            }

            return 0;
        }
    }
}
