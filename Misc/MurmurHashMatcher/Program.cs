using System.Globalization;
using System.Text;
using ValveResourceFormat.ThirdParty;
using ValveResourceFormat.Utils;

Console.Error.WriteLine($"Usage: ./program <path to folder to find vmaps in>");
Console.Error.WriteLine("Alternatively, create strings.txt with one string per line.");
Console.WriteLine();

// Generate with Decompiler:
// .\Source2Viewer-CLI.exe -i "Steam\steamapps\common" --threads 10 --stats --dump_unknown_entity_keys --vpk_extensions "vents_c" --recursive --recursive_vpk
var unknownKeys = File.ReadAllLines("unknown_keys.txt")
    .Select(x => uint.Parse(x, NumberStyles.None, CultureInfo.InvariantCulture))
    .Except(StringToken.InvertedTable.Keys)
    .ToHashSet();
var foundKeys = new List<string>();

if (File.Exists("strings.txt"))
{
    Console.WriteLine($"Scanning strings.txt");

    var strings = File.ReadAllLines("strings.txt");

    foreach (var str in strings)
    {
        var strLower = str.ToLowerInvariant();
        var hash = MurmurHash2.Hash(strLower, StringToken.MURMUR2SEED);

        if (unknownKeys.Remove(hash))
        {
            foundKeys.Add(strLower);
            Console.WriteLine($"Found string: {strLower}");
        }
    }
}

if (args?.Length > 0 && Directory.Exists(args![0]))
{
    var files = new List<string>();
    var allowedExtensions = new HashSet<string>()
    {
        ".vpk",
        ".dll",
        ".so",
        ".dylib",
        ".vmap",
        ".dmx",
    };

    foreach (var file in Directory.EnumerateFiles(args[0], "*.*", SearchOption.AllDirectories))
    {
        if (allowedExtensions.Contains(Path.GetExtension(file)))
        {
            files.Add(file);
        }
    }

    void CheckString(Span<byte> bytes)
    {
        var str = Encoding.UTF8.GetString(bytes).ToLowerInvariant();
        var hash = MurmurHash2.Hash(str, StringToken.MURMUR2SEED);

        if (unknownKeys.Remove(hash))
        {
            foundKeys.Add(str);
            Console.WriteLine($"Found string: {str}");
        }
    }

    Parallel.ForEach(files, new ParallelOptions
    {
        MaxDegreeOfParallelism = 10,
    }, (file) =>
    {
        Console.WriteLine($"Scanning {file}");

        var bytes = File.ReadAllBytes(file).AsSpan();

        while (!bytes.IsEmpty)
        {
            // This is a very rudimentary bruteforce to find all the strings
            var position = bytes.IndexOfAny((byte)0, (byte)'\r', (byte)'\n');

            if (position == -1)
            {
                break;
            }

            var asciiStart = position;

            for (var i = position - 1; i > 0; i--)
            {
                if (bytes[i] < 32 || bytes[i] > 126 || position - i > 50)
                {
                    break;
                }

                var c = (char)bytes[i];

                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    break;
                }

                asciiStart--;

                if (position - asciiStart >= 4)
                {
                    var temp = bytes[asciiStart..position];
                    CheckString(temp);
                }
            }

            if (position - asciiStart >= 4)
            {
                for (var i = asciiStart; i < position; i++)
                {
                    var temp = bytes[i..position];
                    CheckString(temp);
                }
            }

            bytes = bytes[(position + 1)..];
        }
    });
}

if (unknownKeys.Count > 0)
{
    Console.WriteLine($"Did not find {unknownKeys.Count} hashes:");
    Console.WriteLine();

    foreach (var hash in unknownKeys)
    {
        Console.WriteLine(hash);
    }

    Console.WriteLine();
}

Console.WriteLine($"Found {foundKeys.Count} strings:");
Console.WriteLine();

foreach (var key in foundKeys.OrderBy(x => x))
{
    Console.Write('"');
    Console.Write(key);
    Console.Write('"');
    Console.WriteLine(',');
}

return 0;
