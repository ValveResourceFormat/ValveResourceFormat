using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ValvePak;

namespace ValveResourceFormat.IO;

/// <summary>
/// What the text of a <see cref="PackageSearchQuery"/> is matched against.
/// </summary>
public enum PackageSearchType
{
    /// <summary>
    /// The file name, or the full path when the text has a slash.
    /// </summary>
    FileName,

    /// <summary>
    /// The full path of the file.
    /// </summary>
    FullPath,

    /// <summary>
    /// The file contents as UTF-8 text, with the KV3 blocks of compiled files decompressed.
    /// </summary>
    FileContents,

    /// <summary>
    /// The stored file contents as hex bytes, such as <c>56 42 4B 56</c>.
    /// </summary>
    FileContentsHex,
}

/// <summary>
/// The text of a package search and how it matches.
/// </summary>
/// <param name="Text">The text, hex bytes or regular expression to find.</param>
/// <param name="Type">What the text is matched against.</param>
/// <param name="MatchCase">Whether letter case has to match.</param>
/// <param name="WholeWord">Whether the match has to be a whole word.</param>
/// <param name="UseRegex">Whether <paramref name="Text"/> is a regular expression.</param>
public readonly record struct PackageSearchQuery(string Text, PackageSearchType Type, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);

/// <summary>
/// Finds package files by name, path or contents.
/// </summary>
public static class PackageSearch
{
    private const int MaxRegexFileLength = 256 * 1024 * 1024;

    /// <summary>
    /// Returns why the text of a query can not be searched, or <see langword="null"/> when it can.
    /// </summary>
    public static string? GetError(PackageSearchQuery query)
    {
        if (query.Text.Length == 0)
        {
            return null;
        }

        try
        {
            if (query.Type == PackageSearchType.FileContentsHex)
            {
                return ParseHexBytes(query.Text).Length < 3 ? "Enter at least 3 bytes to search file contents" : null;
            }

            CreateRegex(query, query.Text, Regex.InfiniteMatchTimeout);
        }
        catch (FormatException)
        {
            return "Enter pairs of hex digits, such as 56 42 4B 56";
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }

        return query is { Type: PackageSearchType.FileContents, UseRegex: false } && Encoding.UTF8.GetByteCount(query.Text) < 3
            ? "Enter at least 3 characters to search file contents"
            : null;
    }

    /// <summary>
    /// Returns the files that match a query, or all of them when its text is empty.
    /// Names match by file name, or by full path for <see cref="PackageSearchType.FullPath"/> or text with a slash.
    /// Contents are read in parallel in archive order, and for <see cref="PackageSearchType.FileContents"/> compiled resources
    /// are matched by their decompressed blocks, see <see cref="Resource.AnyBlockContents"/>.
    /// </summary>
    /// <param name="package">The package that holds the files.</param>
    /// <param name="candidates">The files to search.</param>
    /// <param name="query">What to find.</param>
    /// <param name="progress">Receives how many of <paramref name="candidates"/> a contents search has done, from any thread.</param>
    /// <param name="cancellationToken">Stops a contents search, which then returns the matches found so far.</param>
    /// <returns>The matches, and how many files could not be searched.</returns>
    public static (List<PackageEntry> Matches, int NotSearched) Find(Package package, IReadOnlyCollection<PackageEntry> candidates, PackageSearchQuery query,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default) => query switch
        {
            { Text: "" } => ([.. candidates], 0),
            { Type: PackageSearchType.FileName or PackageSearchType.FullPath } => FindByName(candidates, query),
            _ => FindByContents(package, candidates, query, progress, cancellationToken),
        };

    private static (List<PackageEntry> Matches, int NotSearched) FindByName(IEnumerable<PackageEntry> entries, PackageSearchQuery query)
    {
        var text = query.UseRegex ? query.Text : query.Text.Replace('\\', Package.DirectorySeparatorChar);
        var fullPath = query.Type == PackageSearchType.FullPath || (!query.UseRegex && text.Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal));
        var regex = query.UseRegex || query.WholeWord ? CreateRegex(query, text, TimeSpan.FromMilliseconds(100)) : null;
        var comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<PackageEntry>();
        var notSearched = 0;

        foreach (var entry in entries)
        {
            if (notSearched > 0)
            {
                notSearched++;
                continue;
            }

            var value = fullPath ? entry.GetFullPath() : entry.GetFileName();

            try
            {
                if (regex?.IsMatch(value) ?? value.Contains(text, comparison))
                {
                    matches.Add(entry);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                notSearched = 1;
            }
        }

        return (matches, notSearched);
    }

    private static (List<PackageEntry> Matches, int NotSearched) FindByContents(Package package, IReadOnlyCollection<PackageEntry> candidates, PackageSearchQuery query,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var match = CreateContentsMatcher(query, out var usesRegex);
        var entries = new List<PackageEntry>(candidates.Count);
        var matches = new List<PackageEntry>();
        var notSearched = 0;

        foreach (var entry in candidates)
        {
            if ((package is NestedPackage && entry.ArchiveIndex != Package.DirectoryArchiveIndex) || (usesRegex && entry.TotalLength > MaxRegexFileLength))
            {
                notSearched++;
            }
            else if (entry.TotalLength > 0)
            {
                entries.Add(entry);
            }
        }

        entries.Sort((a, b) => a.ArchiveIndex != b.ArchiveIndex ? a.ArchiveIndex.CompareTo(b.ArchiveIndex) : a.Offset.CompareTo(b.Offset));

        var done = candidates.Count - entries.Count;
        progress?.Report(done);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = usesRegex ? 3 : Environment.ProcessorCount,
            CancellationToken = cancellationToken,
        };

        try
        {
            Parallel.ForEach(entries, options, entry =>
            {
                try
                {
                    if (IsMatch(package, entry, query.Type == PackageSearchType.FileContents, match))
                    {
                        lock (matches)
                        {
                            matches.Add(entry);
                        }
                    }
                }
                catch (Exception e) when (e is RegexMatchTimeoutException or IOException)
                {
                    Interlocked.Increment(ref notSearched);
                }

                progress?.Report(Interlocked.Increment(ref done));
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return (matches, notSearched);
    }

    private static bool IsMatch(Package package, PackageEntry entry, bool decompress, Func<ReadOnlySpan<byte>, bool> match)
    {
        if (decompress && entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            using var stream = package.GetMemoryMappedStreamIfPossible(entry);

            try
            {
                return Resource.AnyBlockContents(stream, match);
            }
            catch (Exception e) when (e is UnexpectedMagicException or InvalidDataException or EndOfStreamException)
            {
            }
        }

        using var buffer = new RentedBuffer<byte>((int)entry.TotalLength);
        package.ReadEntry(entry, buffer.ByteArray, validateCrc: false);
        return match(buffer.Span);
    }

    private static Func<ReadOnlySpan<byte>, bool> CreateContentsMatcher(PackageSearchQuery query, out bool usesRegex)
    {
        if (query.Type == PackageSearchType.FileContentsHex)
        {
            var bytes = ParseHexBytes(query.Text);
            usesRegex = false;
            return data => data.IndexOf(bytes) >= 0;
        }

        var pattern = Encoding.UTF8.GetBytes(query.Text);
        var prefilter = !query.UseRegex && (query.MatchCase || Ascii.IsValid(pattern));
        var regex = !prefilter || query.WholeWord ? CreateRegex(query, query.Text, TimeSpan.FromSeconds(3)) : null;
        usesRegex = regex != null;

        return data => (!prefilter || (query.MatchCase ? data.IndexOf(pattern) >= 0 : ContainsIgnoreAsciiCase(data, pattern)))
            && (regex == null || IsTextMatch(regex, data));
    }

    private static Regex CreateRegex(PackageSearchQuery query, string text, TimeSpan matchTimeout)
    {
        var pattern = query.UseRegex ? text : Regex.Escape(text);

        if (query.WholeWord)
        {
            pattern = string.Concat(@"(?<!\w)(?:", pattern, @")(?!\w)");
        }

        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);

        return new Regex(pattern, options, matchTimeout);
    }

    private static byte[] ParseHexBytes(string text) => Convert.FromHexString(text.Replace(" ", "", StringComparison.Ordinal));

    private static bool ContainsIgnoreAsciiCase(ReadOnlySpan<byte> data, byte[] pattern)
    {
        var lower = (byte)char.ToLowerInvariant((char)pattern[0]);
        var upper = (byte)char.ToUpperInvariant((char)pattern[0]);

        while (data.IndexOfAny(lower, upper) is var index and >= 0 && data.Length - index >= pattern.Length)
        {
            if (Ascii.EqualsIgnoreCase(data.Slice(index, pattern.Length), pattern))
            {
                return true;
            }

            data = data[(index + 1)..];
        }

        return false;
    }

    private static bool IsTextMatch(Regex regex, ReadOnlySpan<byte> data)
    {
        using var chars = new RentedBuffer<char>(Encoding.UTF8.GetMaxCharCount(data.Length));
        var text = chars.Span[..Encoding.UTF8.GetChars(data, chars.Span)];
        return regex.IsMatch(text is ['\uFEFF', ..] ? text[1..] : text);
    }
}
