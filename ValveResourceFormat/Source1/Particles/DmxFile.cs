using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>A color attribute, one byte per channel.</summary>
internal readonly record struct DmxColor(byte R, byte G, byte B, byte A);

/// <summary>A time attribute, in tenths of a millisecond.</summary>
internal readonly record struct DmxTime(int TenthsOfMilliseconds)
{
    public float Seconds => TenthsOfMilliseconds / 10000f;
}

/// <summary>A reference to an element that is stored in another file.</summary>
internal readonly record struct DmxExternalReference(string Id);

/// <summary>An element of a DMX file: a typed, named bag of attributes.</summary>
internal sealed class DmxElement
{
    public required string Type { get; init; }

    public required string Name { get; set; }

    public Guid Id { get; set; }

    /// <summary>
    /// The attribute values by name. A value is an <see cref="int"/>, <see cref="float"/>, <see cref="bool"/>,
    /// <see cref="string"/>, <see cref="byte"/> array, <see cref="DmxTime"/>, <see cref="DmxColor"/>, vector, quaternion,
    /// <see cref="Matrix4x4"/>, <see cref="DmxElement"/>, <see cref="DmxExternalReference"/>, or a list of one of these.
    /// An element reference that points at nothing is <see langword="null"/>.
    /// </summary>
    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? Get(string name) => Attributes.GetValueOrDefault(name);

    public float GetFloat(string name, float defaultValue = 0f) => Get(name) switch
    {
        float value => value,
        int value => value,
        bool value => value ? 1f : 0f,
        DmxTime value => value.Seconds,
        _ => defaultValue,
    };

    public int GetInt(string name, int defaultValue = 0) => Get(name) switch
    {
        int value => value,
        float value => (int)value,
        bool value => value ? 1 : 0,
        _ => defaultValue,
    };

    public bool GetBool(string name, bool defaultValue = false) => Get(name) switch
    {
        bool value => value,
        int value => value != 0,
        float value => value != 0f,
        _ => defaultValue,
    };

    public string GetString(string name, string defaultValue = "") => Get(name) is string value ? value : defaultValue;

    public Vector3 GetVector3(string name, Vector3 defaultValue = default) => Get(name) is Vector3 value ? value : defaultValue;

    public DmxColor GetColor(string name, DmxColor defaultValue) => Get(name) is DmxColor value ? value : defaultValue;

    public IReadOnlyList<DmxElement> GetElements(string name)
    {
        if (Get(name) is not List<object?> list)
        {
            return [];
        }

        var elements = new List<DmxElement>(list.Count);

        foreach (var item in list)
        {
            if (item is DmxElement element)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    public override string ToString() => $"{Type} \"{Name}\"";
}

/// <summary>
/// Reads Valve DMX files: the binary encoding (versions 1 to 5) and the keyvalues2 text encoding.
/// </summary>
internal static class DmxFile
{
    private const string HeaderStart = "<!-- dmx encoding ";
    private const string LegacyHeaderStart = "<!-- DMXVersion ";

    private const int ElementIndexNull = -1;
    private const int ElementIndexExternal = -2;

    private const int StringTableVersion = 2;
    private const int GlobalStringTableVersion = 4;
    private const int LargeSymbolsVersion = 5;
    private const int MaxBinaryVersion = 5;

    private enum AttributeType : byte
    {
        Element = 1,
        Int,
        Float,
        Bool,
        String,
        Void,
        Time,
        Color,
        Vector2,
        Vector3,
        Vector4,
        QAngle,
        Quaternion,
        Matrix,
        ElementArray,
    }

    // An array type is its element type plus the number of value types
    private const int ArrayOffset = 14;
    private const AttributeType FirstArrayType = AttributeType.ElementArray;

    /// <summary>The encoding and format named by the header line of a file.</summary>
    /// <param name="Encoding">The encoding name, <c>binary</c> or <c>keyvalues2</c>.</param>
    /// <param name="EncodingVersion">The version of the encoding.</param>
    /// <param name="Format">The format name, such as <c>pcf</c>.</param>
    /// <param name="FormatVersion">The version of the format.</param>
    public readonly record struct Header(string Encoding, int EncodingVersion, string Format, int FormatVersion);

    /// <summary>Reads a DMX file and returns its root element.</summary>
    /// <exception cref="InvalidDataException">The data is not a DMX file this reader supports.</exception>
    public static DmxElement Read(ReadOnlySpan<byte> data, out Header header)
    {
        header = ReadHeader(data, out var bodyOffset);

        if (header.Encoding.Equals("binary", StringComparison.OrdinalIgnoreCase))
        {
            return new BinaryReaderState(data[bodyOffset..], header.EncodingVersion).Read();
        }

        if (header.Encoding.Equals("keyvalues2", StringComparison.OrdinalIgnoreCase))
        {
            return DmxTextReader.Read(Encoding.UTF8.GetString(data[bodyOffset..]));
        }

        throw new InvalidDataException($"Unsupported DMX encoding '{header.Encoding}'.");
    }

    /// <summary>Reads a DMX file and returns its root element.</summary>
    public static DmxElement Read(Stream stream, out Header header)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return Read(memory.GetBuffer().AsSpan(0, (int)memory.Length), out header);
    }

    private static Header ReadHeader(ReadOnlySpan<byte> data, out int bodyOffset)
    {
        var lineEnd = data.IndexOf((byte)'\n');

        if (lineEnd < 0 || lineEnd > 256)
        {
            throw new InvalidDataException("Not a DMX file.");
        }

        var line = Encoding.ASCII.GetString(data[..lineEnd]).TrimEnd('\r').Trim();
        bodyOffset = lineEnd + 1;

        // A binary file writes the terminator of the header string too
        if (bodyOffset < data.Length && data[bodyOffset] == 0)
        {
            bodyOffset++;
        }

        if (line.StartsWith(HeaderStart, StringComparison.Ordinal) && line.EndsWith("-->", StringComparison.Ordinal))
        {
            // "binary 2 format pcf 1" once the leading "encoding" is cut
            var parts = line[HeaderStart.Length..^3].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 5 && parts[2] == "format"
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var encodingVersion)
                && int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var formatVersion))
            {
                return new Header(parts[0], encodingVersion, parts[3], formatVersion);
            }
        }

        if (line.StartsWith(LegacyHeaderStart, StringComparison.Ordinal) && line.EndsWith("-->", StringComparison.Ordinal))
        {
            var format = line[LegacyHeaderStart.Length..^3].Trim();

            return format switch
            {
                "binary_v1" or "binary_v2" => new Header("binary", 0, format, 0),
                "keyvalues2_v1" or "keyvalues2_flat_v1" => new Header("keyvalues2", 1, format, 0),
                _ => throw new InvalidDataException($"Unsupported legacy DMX format '{format}'."),
            };
        }

        throw new InvalidDataException("Not a DMX file.");
    }

    private ref struct BinaryReaderState(ReadOnlySpan<byte> data, int version)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;
        private string[]? strings;

        public DmxElement Read()
        {
            if (version < 0 || version > MaxBinaryVersion)
            {
                throw new InvalidDataException($"Unsupported binary DMX encoding version {version}.");
            }

            if (version >= StringTableVersion)
            {
                var stringCount = version >= GlobalStringTableVersion ? ReadInt32() : ReadInt16();
                strings = ReadStringTable(stringCount);
            }

            var elementCount = ReadInt32();

            if (elementCount <= 0)
            {
                throw new InvalidDataException("DMX file has no elements.");
            }

            // Every element has at least a type, a name and an id
            if (elementCount > data.Length)
            {
                throw new InvalidDataException("DMX element count is larger than the file.");
            }

            var elements = new DmxElement[elementCount];

            for (var i = 0; i < elementCount; i++)
            {
                var type = ReadSymbol();
                var name = version >= GlobalStringTableVersion ? ReadSymbol() : ReadString();
                var id = new Guid(ReadBytes(16));

                elements[i] = new DmxElement { Type = type, Name = name, Id = id };
            }

            foreach (var element in elements)
            {
                ReadAttributes(element, elements);
            }

            return elements[0];
        }

        private string[] ReadStringTable(int count)
        {
            if (count < 0 || count > data.Length)
            {
                throw new InvalidDataException("DMX string table is larger than the file.");
            }

            var table = new string[count];

            for (var i = 0; i < count; i++)
            {
                table[i] = ReadString();
            }

            return table;
        }

        private void ReadAttributes(DmxElement element, DmxElement[] elements)
        {
            var attributeCount = ReadInt32();

            if (attributeCount < 0 || attributeCount > data.Length)
            {
                throw new InvalidDataException("DMX attribute count is larger than the file.");
            }

            for (var i = 0; i < attributeCount; i++)
            {
                var name = ReadSymbol();
                var type = (AttributeType)ReadByte();

                // Version 2 files can still hold the object id attribute that version 3 replaced with the time type
                if (type == AttributeType.Time && version < 3)
                {
                    ReadBytes(16);
                    continue;
                }

                element.Attributes[name] = ReadValue(type, elements);
            }
        }

        private object? ReadValue(AttributeType type, DmxElement[] elements)
        {
            if (type >= FirstArrayType)
            {
                return ReadArray((AttributeType)((int)type - ArrayOffset), elements);
            }

            return type switch
            {
                AttributeType.Element => ReadElementReference(elements),
                AttributeType.Int => ReadInt32(),
                AttributeType.Float => ReadSingle(),
                AttributeType.Bool => ReadByte() != 0,
                AttributeType.String => version >= GlobalStringTableVersion ? ReadSymbol() : ReadString(),
                AttributeType.Void => ReadBytes(ReadInt32()).ToArray(),
                AttributeType.Time => new DmxTime(ReadInt32()),
                AttributeType.Color => new DmxColor(ReadByte(), ReadByte(), ReadByte(), ReadByte()),
                AttributeType.Vector2 => new Vector2(ReadSingle(), ReadSingle()),
                AttributeType.Vector3 or AttributeType.QAngle => new Vector3(ReadSingle(), ReadSingle(), ReadSingle()),
                AttributeType.Vector4 or AttributeType.Quaternion => new Vector4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle()),
                AttributeType.Matrix => ReadMatrix(),
                _ => throw new InvalidDataException($"Unknown DMX attribute type {(int)type}."),
            };
        }

        private List<object?> ReadArray(AttributeType elementType, DmxElement[] elements)
        {
            var count = ReadInt32();

            if (count < 0 || count > data.Length)
            {
                throw new InvalidDataException("DMX array is larger than the file.");
            }

            var list = new List<object?>(count);

            for (var i = 0; i < count; i++)
            {
                // The elements of a string array are always stored inline
                list.Add(elementType == AttributeType.String ? ReadString() : ReadValue(elementType, elements));
            }

            return list;
        }

        private object? ReadElementReference(DmxElement[] elements)
        {
            var index = ReadInt32();

            if (index == ElementIndexExternal)
            {
                return new DmxExternalReference(ReadString());
            }

            if (index == ElementIndexNull)
            {
                return null;
            }

            if (index < 0 || index >= elements.Length)
            {
                throw new InvalidDataException("DMX element reference is out of range.");
            }

            return elements[index];
        }

        private Matrix4x4 ReadMatrix()
        {
            return new Matrix4x4(
                ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
                ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        // Names and types come from the string table when the file has one
        private string ReadSymbol()
        {
            if (strings == null)
            {
                return ReadString();
            }

            var index = version >= LargeSymbolsVersion ? ReadInt32() : ReadInt16();

            if ((uint)index >= (uint)strings.Length)
            {
                throw new InvalidDataException("DMX string index is out of range.");
            }

            return strings[index];
        }

        private string ReadString()
        {
            var remaining = data[position..];
            var length = remaining.IndexOf((byte)0);

            if (length < 0)
            {
                throw new InvalidDataException("DMX string is not terminated.");
            }

            var value = Encoding.UTF8.GetString(remaining[..length]);
            position += length + 1;

            return value;
        }

        private ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > data.Length - position)
            {
                throw new InvalidDataException("Unexpected end of DMX data.");
            }

            var bytes = data.Slice(position, count);
            position += count;

            return bytes;
        }

        private byte ReadByte() => ReadBytes(1)[0];

        private short ReadInt16() => BitConverter.ToInt16(ReadBytes(2));

        private int ReadInt32() => BitConverter.ToInt32(ReadBytes(4));

        private float ReadSingle() => BitConverter.ToSingle(ReadBytes(4));
    }
}
