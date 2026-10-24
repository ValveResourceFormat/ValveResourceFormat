using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GUI.Forms;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Finds package files by name, path or contents.
    /// </summary>
    internal static class PackageSearch
    {
        private const int MaxRegexFileLength = 256 * 1024 * 1024;

        /// <summary>
        /// Returns why the text of a search can not be searched, or <see langword="null"/> when it can.
        /// </summary>
        public static string? GetError(SearchRequest request)
        {
            if (request.Text.Length == 0)
            {
                return null;
            }

            try
            {
                if (request.Type == SearchType.FileContentsHex)
                {
                    return ParseHexBytes(request.Text).Length < 3 ? "Enter at least 3 bytes to search file contents" : null;
                }

                CreateRegex(request, request.Text, Regex.InfiniteMatchTimeout);
            }
            catch (FormatException)
            {
                return "Enter pairs of hex digits, such as 56 42 4B 56";
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }

            return request is { Type: SearchType.FileContents, UseRegex: false } && Encoding.UTF8.GetByteCount(request.Text) < 3
                ? "Enter at least 3 characters to search file contents"
                : null;
        }

        /// <summary>
        /// Returns the files whose name matches, or whose full path matches for a path search or text with a slash.
        /// </summary>
        /// <returns>The matches, and how many files were not searched after the regular expression took too long.</returns>
        public static (List<PackageEntry> Matches, int NotSearched, bool Cancelled) FindByName(IEnumerable<PackageEntry> entries, SearchRequest request)
        {
            var text = request.UseRegex ? request.Text : request.Text.Replace('\\', Package.DirectorySeparatorChar);
            var fullPath = request.Type == SearchType.FullPath || (!request.UseRegex && text.Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal));
            var regex = request.UseRegex || request.WholeWord ? CreateRegex(request, text, TimeSpan.FromMilliseconds(100)) : null;
            var comparison = request.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
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

            return (matches, notSearched, false);
        }

        /// <summary>
        /// Reads the files in parallel in archive order behind a progress dialog and returns those whose contents match.
        /// Compiled resources are matched by their decompressed blocks, see <see cref="Resource.AnyBlockContents"/>.
        /// </summary>
        /// <returns>The matches, how many files could not be searched, and whether the user cancelled the search.</returns>
        public static (List<PackageEntry> Matches, int NotSearched, bool Cancelled) FindByContents(Package package, IEnumerable<PackageEntry> candidates, SearchRequest request)
        {
            var match = CreateContentsMatcher(request, out var usesRegex);
            var entries = new List<PackageEntry>();
            var matches = new List<PackageEntry>();
            var notSearched = 0;
            var searched = 0;
            var cancelled = false;

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

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents…",
                WaitForWorkToStop = true,
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                progressDialog.SetProgress(string.Create(CultureInfo.InvariantCulture, $"Searching {entries.Count:N0} files"));
                progressDialog.SetBarMax(entries.Count);

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
                            if (IsMatch(package, entry, request.Type == SearchType.FileContents, match))
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

                        progressDialog.SetBarValue(Interlocked.Increment(ref searched));
                    });
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();

            return (matches, notSearched, cancelled);
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

            var length = (int)entry.TotalLength;
            var buffer = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                package.ReadEntry(entry, buffer, validateCrc: false);
                return match(buffer.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static Func<ReadOnlySpan<byte>, bool> CreateContentsMatcher(SearchRequest request, out bool usesRegex)
        {
            if (request.Type == SearchType.FileContentsHex)
            {
                var bytes = ParseHexBytes(request.Text);
                usesRegex = false;
                return data => data.IndexOf(bytes) >= 0;
            }

            var pattern = Encoding.UTF8.GetBytes(request.Text);
            var prefilter = !request.UseRegex && (request.MatchCase || Ascii.IsValid(pattern));
            var regex = !prefilter || request.WholeWord ? CreateRegex(request, request.Text, TimeSpan.FromSeconds(3)) : null;
            usesRegex = regex != null;

            return data => (!prefilter || (request.MatchCase ? data.IndexOf(pattern) >= 0 : ContainsIgnoreAsciiCase(data, pattern)))
                && (regex == null || IsTextMatch(regex, data));
        }

        private static Regex CreateRegex(SearchRequest request, string text, TimeSpan matchTimeout)
        {
            var pattern = request.UseRegex ? text : Regex.Escape(text);

            if (request.WholeWord)
            {
                pattern = string.Concat(@"(?<!\w)(?:", pattern, @")(?!\w)");
            }

            var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (request.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);

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
            var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(data.Length));

            try
            {
                var text = chars.AsSpan(0, Encoding.UTF8.GetChars(data, chars));
                return regex.IsMatch(text is ['\uFEFF', ..] ? text[1..] : text);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }
    }
}
