using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ValveKeyValue;

namespace CLI
{
    /// <summary>
    /// Prints KeyValues text as one "path = value" line per leaf, so that two versions of a file can be compared with a plain line diff.
    /// </summary>
    internal sealed class KeyValuesFlattener
    {
        // Array elements are keyed by the first of these members they have, so that removing one element does not renumber the others.
        // Names come before the class, because elements of the same class (such as particle operators) are often repeated.
        private static readonly string[] ArrayElementIdentifiers = ["m_strPropertyName", "m_name", "name", "m_strName", "m_Name", "_class"];

        private static readonly string ControlCharacters = string.Create(32, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (char)i;
            }
        });

        private static readonly SearchValues<char> EscapedCharacters = SearchValues.Create("\"\\" + ControlCharacters);

        // Characters that would make a bare key ambiguous with the path syntax
        private static readonly SearchValues<char> KeyQuotedCharacters = SearchValues.Create(" .\"[]\\#=" + ControlCharacters);
        private static readonly SearchValues<char> IntegerCharacters = SearchValues.Create("-0123456789");

        private readonly TextWriter Writer;

        private KeyValuesFlattener(TextWriter writer)
        {
            Writer = writer;
        }

        /// <summary>
        /// Flattens UTF-8 KeyValues1 or KeyValues3 text, detected by the KV3 header comment.
        /// </summary>
        public static void Flatten(byte[] data, TextWriter writer)
        {
            var format = IsKeyValues3Text(data)
                ? KVSerializationFormat.KeyValues3Text
                : KVSerializationFormat.KeyValues1Text;

            var document = Deserialize(data, format);
            var flattener = new KeyValuesFlattener(writer);

            if (format == KVSerializationFormat.KeyValues1Text)
            {
                flattener.WriteIncludes(data);
            }

            // The KV1 root key names the file, so it is left out of every path
            flattener.WriteCollections(string.Empty, [document.Root]);
        }

        /// <summary>
        /// Thrown by <see cref="KVSerializer"/> for malformed text.
        /// </summary>
        public static bool IsParseException(Exception e) => e is KeyValueException or InvalidDataException or InvalidOperationException;

        private static bool IsKeyValues3Text(ReadOnlySpan<byte> data)
        {
            if (data.StartsWith(Encoding.UTF8.Preamble))
            {
                data = data[Encoding.UTF8.Preamble.Length..];
            }

            return data.TrimStart(" \t\r\n"u8).StartsWith("<!-- kv3"u8);
        }

        private static KVDocument Deserialize(byte[] data, KVSerializationFormat format)
        {
            try
            {
                return Deserialize(data, format, escapeSequences: false);
            }
            catch (Exception e) when (format == KVSerializationFormat.KeyValues1Text && IsParseException(e))
            {
                // Localization files escape quotes, while most other files have unescaped backslashes in paths
                try
                {
                    return Deserialize(data, format, escapeSequences: true);
                }
                catch (Exception retry) when (IsParseException(retry))
                {
                    ExceptionDispatchInfo.Throw(e);
                    throw;
                }
            }
        }

        private static KVDocument Deserialize(byte[] data, KVSerializationFormat format, bool escapeSequences)
        {
            var options = new KVSerializerOptions
            {
                HasEscapeSequences = escapeSequences,

                // Strings are cut off at unknown escapes like "\x00A2", the same as the game reads them
                EnableValveNullByteBugBehavior = escapeSequences,

                // Included files are not resolved, there is nothing to resolve them against when reading stdin
                FileLoader = new EmptyIncludedFileLoader(),
            };

            using var stream = new MemoryStream(data, writable: false);
            return KVSerializer.Create(format).Deserialize(stream, options);
        }

        private sealed class EmptyIncludedFileLoader : IIncludedFileLoader
        {
            public Stream OpenFile(string filePath) => new MemoryStream("\"\" {}"u8.ToArray());
        }

        /// <summary>
        /// The object model does not keep include directives, but they are only allowed at the start of the file.
        /// </summary>
        private void WriteIncludes(byte[] data)
        {
            using var reader = new StreamReader(new MemoryStream(data, writable: false), Encoding.UTF8);

            while (reader.ReadLine() is { } line)
            {
                var directive = line.AsSpan().Trim();

                if (directive.IsEmpty || directive.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (directive[0] != '#')
                {
                    break;
                }

                var separator = directive.IndexOfAny(' ', '\t');

                if (separator < 0)
                {
                    break;
                }

                var file = directive[separator..].Trim().Trim('"');
                WriteLine(directive[..separator].ToString(), Quote(file.ToString()));
            }
        }

        private void WriteObject(string path, KVObject value)
        {
            switch (value.ValueType)
            {
                case KVValueType.Collection when value.Count == 0:
                    WriteLine(path, "{}");
                    break;

                case KVValueType.Array when value.Count == 0:
                    WriteLine(path, "[]");
                    break;

                case KVValueType.Collection:
                    WriteCollections(path, [value]);
                    break;

                case KVValueType.Array:
                    WriteArray(path, value);
                    break;

                default:
                    WriteLine(path, FormatValue(value));
                    break;
            }
        }

        private void WriteArray(string path, KVObject array)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var index = 0;

            foreach (var element in array.Values)
            {
                var segment = $"[{GetArrayElementIdentifier(element) ?? index.ToString(CultureInfo.InvariantCulture)}]";
                WriteObject(path + MakeUnique(occurrences, segment), element);
                index++;
            }
        }

        /// <summary>
        /// Writes the children of one or more collections under the same path. Sibling collections
        /// with the same key (KV1 files can split a block like "items" into several) are merged,
        /// so that adding or removing one of them does not renumber the others.
        /// </summary>
        private void WriteCollections(string path, List<KVObject> collections)
        {
            var blocks = new Dictionary<string, List<KVObject>>(StringComparer.Ordinal);

            foreach (var collection in collections)
            {
                foreach (var (key, child) in collection.Children)
                {
                    if (child.IsCollection)
                    {
                        ref var group = ref CollectionsMarshal.GetValueRefOrAddDefault(blocks, key, out _);
                        group ??= [];
                        group.Add(child);
                    }
                }
            }

            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var collection in collections)
            {
                foreach (var (key, child) in collection.Children)
                {
                    var segment = FormatKeySegment(path, key);

                    if (!child.IsCollection)
                    {
                        WriteObject(path + MakeUnique(occurrences, segment), child);
                        continue;
                    }

                    // Write all blocks with this key at the position of the first one
                    var group = blocks[key];

                    if (group[0] != child)
                    {
                        continue;
                    }

                    if (group.TrueForAll(block => block.Count == 0))
                    {
                        WriteLine(path + segment, "{}");
                    }
                    else
                    {
                        WriteCollections(path + segment, group);
                    }
                }
            }
        }

        private static string? GetArrayElementIdentifier(KVObject element)
        {
            if (!element.IsCollection)
            {
                return null;
            }

            foreach (var identifier in ArrayElementIdentifiers)
            {
                if (element.TryGetValue(identifier, out var value) && value.ValueType == KVValueType.String)
                {
                    var name = (string)value;
                    return NeedsQuoting(name) ? Quote(name) : name;
                }
            }

            return null;
        }

        private static string FormatValue(KVObject value) => value.ValueType switch
        {
            KVValueType.Null => "null",
            KVValueType.Boolean => (bool)value ? "true" : "false",
            KVValueType.String => Quote((string)value),
            KVValueType.FloatingPoint => FormatFloat(((float)value).ToString(CultureInfo.InvariantCulture)),
            KVValueType.FloatingPoint64 => FormatFloat(((double)value).ToString(CultureInfo.InvariantCulture)),
            KVValueType.BinaryBlob => FormatBlob(value.AsBlob()),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        // Keep floats distinguishable from integers, so a type change shows up in the diff
        private static string FormatFloat(string text)
            => text.AsSpan().ContainsAnyExcept(IntegerCharacters) ? text : $"{text}.0";

        private static string FormatBlob(byte[] data)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(data).AsSpan(0, 8));
            return $"#[{data.Length} bytes, sha256 {hash}]";
        }

        private static string MakeUnique(Dictionary<string, int> occurrences, string segment)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(occurrences, segment, out _);
            count++;

            return count == 1 ? segment : $"{segment}#{count}";
        }

        private static string FormatKeySegment(string path, string key)
        {
            if (NeedsQuoting(key))
            {
                return $"[{Quote(key)}]";
            }

            return path.Length == 0 ? key : $".{key}";
        }

        private static bool NeedsQuoting(string key)
            => key.Length == 0 || char.IsAsciiDigit(key[0]) || key.AsSpan().ContainsAny(KeyQuotedCharacters);

        private static string Quote(string value)
        {
            if (!value.AsSpan().ContainsAny(EscapedCharacters))
            {
                return $"\"{value}\"";
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case < ' ': builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"); break;
                    default: builder.Append(c); break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private void WriteLine(string path, string value)
        {
            Writer.Write(path);
            Writer.Write(" = ");
            Writer.WriteLine(value);
        }
    }
}
