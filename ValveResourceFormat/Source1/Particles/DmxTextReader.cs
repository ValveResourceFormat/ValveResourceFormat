using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>Reads the keyvalues2 text encoding of DMX.</summary>
internal sealed class DmxTextReader
{
    private readonly string text;
    private int position;
    private readonly Dictionary<Guid, DmxElement> elementsById = [];
    private readonly List<(Dictionary<string, object?> Attributes, string Name, int Index, Guid Id)> pendingReferences = [];

    private DmxTextReader(string text)
    {
        this.text = text;
    }

    public static DmxElement Read(string text)
    {
        var reader = new DmxTextReader(text);
        var root = reader.ReadElement(reader.ReadToken() ?? throw new InvalidDataException("DMX text file is empty."));
        reader.ResolveReferences();

        return root;
    }

    private DmxElement ReadElement(string type)
    {
        Expect("{");

        var element = new DmxElement { Type = type, Name = string.Empty };

        while (true)
        {
            var attributeName = ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text.");

            if (attributeName == "}")
            {
                break;
            }

            var attributeType = ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text.");

            if (attributeName == "id" && attributeType == "elementid")
            {
                element.Id = Guid.Parse(ReadToken()!);
                elementsById[element.Id] = element;
                continue;
            }

            if (attributeName == "name" && attributeType == "string")
            {
                element.Name = ReadToken()!;
                continue;
            }

            element.Attributes[attributeName] = ReadAttributeValue(attributeType, element, attributeName);
        }

        return element;
    }

    private object? ReadAttributeValue(string type, DmxElement owner, string attributeName)
    {
        if (type == "element")
        {
            return ReadReference(owner, attributeName, -1);
        }

        if (type == "element_array")
        {
            Expect("[");

            var items = new List<object?>();

            while (PeekToken() != "]")
            {
                var itemType = ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text.");

                if (itemType == "element")
                {
                    items.Add(ReadReference(owner, attributeName, items.Count));
                }
                else
                {
                    items.Add(ReadElement(itemType));
                }

                if (PeekToken() == ",")
                {
                    ReadToken();
                }
            }

            ReadToken();

            return items;
        }

        if (type.EndsWith("_array", StringComparison.Ordinal))
        {
            var scalarType = type[..^"_array".Length];
            var items = new List<object?>();

            Expect("[");

            while (PeekToken() != "]")
            {
                items.Add(ParseScalar(scalarType, ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text.")));

                if (PeekToken() == ",")
                {
                    ReadToken();
                }
            }

            ReadToken();

            return items;
        }

        if (PeekToken() == "{")
        {
            // An element attribute is written as the type name of the element followed by its body
            return ReadElement(type);
        }

        return ParseScalar(type, ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text."));
    }

    private DmxExternalReference? ReadReference(DmxElement owner, string attributeName, int index)
    {
        var idText = ReadToken() ?? throw new InvalidDataException("Unexpected end of DMX text.");

        if (idText.Length == 0)
        {
            return null;
        }

        if (!Guid.TryParse(idText, out var id))
        {
            return new DmxExternalReference(idText);
        }

        pendingReferences.Add((owner.Attributes, attributeName, index, id));

        return null;
    }

    private void ResolveReferences()
    {
        foreach (var (attributes, name, index, id) in pendingReferences)
        {
            if (!elementsById.TryGetValue(id, out var target))
            {
                continue;
            }

            if (index < 0)
            {
                attributes[name] = target;
            }
            else if (attributes.GetValueOrDefault(name) is List<object?> list && index < list.Count)
            {
                list[index] = target;
            }
        }
    }

    private static object ParseScalar(string type, string value)
    {
        var culture = CultureInfo.InvariantCulture;

        return type switch
        {
            "int" => int.Parse(value, culture),
            "float" => float.Parse(value, culture),
            "bool" => value is "1" or "true",
            "string" => value,
            "time" => new DmxTime((int)MathF.Round(float.Parse(value, culture) * 10000f)),
            "color" => ParseColor(value),
            "vector2" => ParseVector2(value),
            "vector3" or "qangle" => ParseVector3(value),
            "vector4" or "quaternion" => ParseVector4(value),
            "matrix" => ParseMatrix(value),
            "binary" => Convert.FromHexString(value.Replace(" ", string.Empty, StringComparison.Ordinal)),
            _ => throw new InvalidDataException($"Unknown DMX attribute type '{type}'."),
        };
    }

    private static float[] ParseFloats(string value, int count)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < count)
        {
            throw new InvalidDataException($"Expected {count} numbers but found '{value}'.");
        }

        var values = new float[count];

        for (var i = 0; i < count; i++)
        {
            values[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static Vector2 ParseVector2(string value)
    {
        var floats = ParseFloats(value, 2);
        return new Vector2(floats[0], floats[1]);
    }

    private static Vector3 ParseVector3(string value)
    {
        var floats = ParseFloats(value, 3);
        return new Vector3(floats[0], floats[1], floats[2]);
    }

    private static Vector4 ParseVector4(string value)
    {
        var floats = ParseFloats(value, 4);
        return new Vector4(floats[0], floats[1], floats[2], floats[3]);
    }

    private static Matrix4x4 ParseMatrix(string value)
    {
        var f = ParseFloats(value, 16);
        return new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
    }

    private static DmxColor ParseColor(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var channels = new byte[4] { 0, 0, 0, 255 };

        for (var i = 0; i < Math.Min(parts.Length, 4); i++)
        {
            channels[i] = byte.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return new DmxColor(channels[0], channels[1], channels[2], channels[3]);
    }

    private void Expect(string token)
    {
        var read = ReadToken();

        if (read != token)
        {
            throw new InvalidDataException($"Expected '{token}' in DMX text but found '{read}'.");
        }
    }

    private string? PeekToken()
    {
        var start = position;
        var token = ReadToken();
        position = start;

        return token;
    }

    // Quoted strings, and the single character tokens { } [ ] ,
    private string? ReadToken()
    {
        while (position < text.Length)
        {
            var c = text[position];

            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            break;
        }

        if (position >= text.Length)
        {
            return null;
        }

        var first = text[position];

        if (first is '{' or '}' or '[' or ']' or ',')
        {
            position++;
            return first.ToString();
        }

        if (first != '"')
        {
            throw new InvalidDataException($"Unexpected character '{first}' in DMX text.");
        }

        position++;

        var builder = new StringBuilder();

        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
            }

            builder.Append(text[position]);
            position++;
        }

        position++;

        return builder.ToString();
    }
}
