using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using ValveResourceFormat.Utils;

Console.Error.WriteLine($"Usage: ./program <path to folder to find strings in>");
Console.Error.WriteLine("Alternatively, create strings.txt with one string per line.");
Console.WriteLine();

// Generate with Decompiler:
// .\Source2Viewer-CLI.exe -i "Steam\steamapps\common" --threads 10 --test --test_entity_keys --vpk_extensions "vents_c" --recursive --recursive_vpk
var unknownKeys = File.ReadAllLines("unknown_keys.txt")
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => uint.Parse(x, NumberStyles.None, CultureInfo.InvariantCulture))
    .ToHashSet();
var foundKeys = new HashSet<string>();

// Valid key bytes; used to vectorize run scanning.
var keyBytes = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-$"u8);

// Prepass: drop any hashes that are already known in code (EntityLumpKnownKeys).
var alreadyKnown = unknownKeys.RemoveWhere(StringToken.InvertedTable.ContainsKey);

if (alreadyKnown > 0)
{
    Console.WriteLine($"Removed {alreadyKnown} hashes already present in the known keys list");
}

// Only read from here on, millions of times during the scan.
var scanKeys = unknownKeys.ToFrozenSet();

if (File.Exists("strings.txt"))
{
    Console.WriteLine($"Scanning strings.txt");

    foreach (var str in File.ReadAllLines("strings.txt"))
    {
        CheckCandidate(str, "strings.txt");
    }
}

if (args is not { Length: > 0 })
{
    return 1;
}

var folder = Path.GetFullPath(args[0]);

if (!Directory.Exists(folder))
{
    Console.WriteLine($"Folder {folder} does not exist");
    return 1;
}

// Prepass: bruteforce the entire keyspace for short keys that the file scan skips.
BruteForceShortKeys(4);

var files = new List<string>();
var allowedExtensions = new HashSet<string>()
{
    ".vpk",
    ".dll",
    ".so",
    ".dylib",
    ".vmap",
    ".dmx",
    ".fgd",
};

foreach (var file in Directory.EnumerateFiles(Path.GetFullPath(args[0]), "*.*", SearchOption.AllDirectories))
{
    if (allowedExtensions.Contains(Path.GetExtension(file)))
    {
        files.Add(file);
    }
}

Console.WriteLine($"Scanning {files.Count} files in {args[0]}");

var scannedFiles = 0;

Parallel.ForEach(files, ScanFile);

// Hashes are not removed on a hit, so derive the misses by subtracting what we found.
var foundHashes = foundKeys.Select(key => StringToken.Get(key)).ToHashSet();
var notFoundHashes = unknownKeys.Where(hash => !foundHashes.Contains(hash)).ToList();

if (notFoundHashes.Count > 0)
{
    Console.WriteLine($"Did not find {notFoundHashes.Count} hashes:");
    Console.WriteLine();

    foreach (var hash in notFoundHashes)
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

void ScanFile(string file)
{
    var bytes = File.ReadAllBytes(file).AsSpan();
    var pos = 0;

    // Reusable window buffer, no key is longer than this many characters.
    Span<char> window = stackalloc char[64];

    while (pos < bytes.Length)
    {
        // Test every window inside a run of valid bytes, so packed strings (no delimiter) are found too.
        var relativeStart = bytes[pos..].IndexOfAny(keyBytes);

        if (relativeStart < 0)
        {
            break;
        }

        var runStart = pos + relativeStart;
        var relativeEnd = bytes[runStart..].IndexOfAnyExcept(keyBytes);
        var runEnd = relativeEnd < 0 ? bytes.Length : runStart + relativeEnd;

        for (var start = runStart; start < runEnd; start++)
        {
            var maxLength = Math.Min(window.Length, runEnd - start);
            Ascii.ToUtf16(bytes.Slice(start, maxLength), window, out _);

            // Lengths up to 4 are already covered exhaustively by BruteForceShortKeys.
            for (var length = 5; length <= maxLength; length++)
            {
                CheckCandidate(window[..length], file);
            }
        }

        pos = runEnd;
    }

    var scanned = Interlocked.Increment(ref scannedFiles);

    if (scanned % 100 == 0)
    {
        Console.WriteLine($"Scanned {scanned}/{files.Count} files - {file}");
    }
}

void CheckCandidate(ReadOnlySpan<char> candidate, string file)
{
    var hash = StringToken.Get(candidate);

    // We don't remove on a hit so that hash collisions surface every matching string.
    if (!scanKeys.Contains(hash))
    {
        return;
    }

    var str = candidate.ToString().ToLowerInvariant();

    lock (foundKeys)
    {
        if (foundKeys.Add(str))
        {
            Console.WriteLine($"Found {hash} string: \"{str}\" in {file}");
        }
    }
}

void BruteForceShortKeys(int maxLength)
{
    Console.WriteLine($"Bruteforcing the keyspace up to {maxLength} characters");

    ReadOnlySpan<char> alphabet = "abcdefghijklmnopqrstuvwxyz0123456789._-$";
    Span<char> buffer = stackalloc char[maxLength];

    for (var length = 1; length <= maxLength; length++)
    {
        var indices = new int[length];

        while (true)
        {
            for (var i = 0; i < length; i++)
            {
                buffer[i] = alphabet[indices[i]];
            }

            CheckCandidate(buffer[..length], "bruteforce");

            var pos = length - 1;

            while (pos >= 0 && ++indices[pos] == alphabet.Length)
            {
                indices[pos] = 0;
                pos--;
            }

            if (pos < 0)
            {
                break;
            }
        }
    }
}
