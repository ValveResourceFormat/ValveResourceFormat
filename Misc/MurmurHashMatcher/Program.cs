using System.Globalization;
using System.Text;
using ValveResourceFormat.ThirdParty;
using ValveResourceFormat.Utils;

Console.Error.WriteLine($"Usage: ./program <path to folder to find vmaps in>");
Console.Error.WriteLine("Alternatively, create strings.txt with one string per line.");
Console.WriteLine();

// Generate with Decompiler:
// .\Decompiler.exe -i "Steam\steamapps\common" --threads 10 --stats --dump_unknown_entity_keys --vpk_extensions "vents_c" --recursive --recursive_vpk
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
    foreach (var file in Directory.EnumerateFiles(args[0], "*.vmap", SearchOption.AllDirectories))
    {
        Console.WriteLine($"Scanning {file}");

        var bytes = File.ReadAllBytes(file).AsSpan();

        while (!bytes.IsEmpty)
        {
            // This is a very rudimentary bruteforce over vmap files to find all the strings in it
            var position = bytes.IndexOf((byte)0);

            if (position == -1)
            {
                break;
            }

            var strBytes = bytes[0..position];
            var str = Encoding.UTF8.GetString(strBytes).ToLowerInvariant();
            var hash = MurmurHash2.Hash(str, StringToken.MURMUR2SEED);

            if (unknownKeys.Remove(hash))
            {
                foundKeys.Add(str);
                Console.WriteLine($"Found string: {str}");
            }

            bytes = bytes[(position + 1)..];
        }
    }
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
