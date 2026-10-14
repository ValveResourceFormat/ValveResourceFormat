using System.Globalization;
using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Basic type of a <see cref="SmartPropValue"/>, mirroring the KeyValues3 basic types a smart prop variable can hold.
    /// </summary>
    public enum SmartPropValueKind
    {
        /// <summary>No value; reads as 0, false, an empty string or a zero vector.</summary>
        Null,
        /// <summary>Boolean.</summary>
        Bool,
        /// <summary>Signed or unsigned integer.</summary>
        Int,
        /// <summary>Double precision float.</summary>
        Float,
        /// <summary>String.</summary>
        String,
        /// <summary>Array of numbers (vectors, angles and colors).</summary>
        Array,
    }

    /// <summary>
    /// Value type of a smart prop variable or attribute (<c>SmartPropVariableValueType</c>).
    /// </summary>
    public enum SmartPropVariableType
    {
        /// <summary>Untyped KeyValues3 value.</summary>
        Invalid,
        /// <summary>String, also every resource name, material group and enum variable.</summary>
        String,
        /// <summary>Boolean.</summary>
        Bool,
        /// <summary>Integer.</summary>
        Int,
        /// <summary>Float.</summary>
        Float,
        /// <summary>Two component vector.</summary>
        Vector2,
        /// <summary>Three component vector.</summary>
        Vector3,
        /// <summary>Four component vector.</summary>
        Vector4,
        /// <summary>RGBA color, bytes.</summary>
        Color,
        /// <summary>Pitch, yaw and roll in degrees.</summary>
        Angles,
    }

    /// <summary>
    /// A KeyValues3-like value held by a smart prop variable. Reading it as another type converts the way
    /// KeyValues3 does: numbers truncate, strings must parse as a whole, and vectors and colors need an exact
    /// component count.
    /// </summary>
    public readonly struct SmartPropValue
    {
        /// <summary>The null value.</summary>
        public static SmartPropValue Null => default;

        /// <summary>Basic type of the value.</summary>
        public SmartPropValueKind Kind { get; }

        private readonly long integer;
        private readonly double number;
        private readonly string? text;
        private readonly double[]? items;
        private readonly bool isUnsigned;
        private readonly bool hasIntegerItems;

        private SmartPropValue(SmartPropValueKind kind, long integer = 0, double number = 0, string? text = null, double[]? items = null, bool isUnsigned = false, bool hasIntegerItems = false)
        {
            Kind = kind;
            this.integer = integer;
            this.number = number;
            this.text = text;
            this.items = items;
            this.isUnsigned = isUnsigned;
            this.hasIntegerItems = hasIntegerItems;
        }

        /// <summary>Creates a boolean value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromBool(bool value) => new(SmartPropValueKind.Bool, number: value ? 1 : 0);

        /// <summary>Creates an integer value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromInt(long value) => new(SmartPropValueKind.Int, integer: value);

        /// <summary>Creates a float value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromFloat(double value) => new(SmartPropValueKind.Float, number: value);

        /// <summary>Creates a string value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromString(string value) => new(SmartPropValueKind.String, text: value);

        /// <summary>Creates an array of floats.</summary>
        /// <param name="values">The array elements.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromArray(params double[] values) => new(SmartPropValueKind.Array, items: values);

        /// <summary>Creates a three component vector value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromVector(Vector3 value) => FromArray(value.X, value.Y, value.Z);

        /// <summary>Creates a color value, an integer array with three elements when fully opaque.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromColor(Color32 value) => new(
            SmartPropValueKind.Array,
            items: value.A == 255 ? [value.R, value.G, value.B] : [value.R, value.G, value.B, value.A],
            hasIntegerItems: true);

        /// <summary>
        /// Converts a KeyValues3 value. Tables and unsupported types become <see cref="Null"/>.
        /// </summary>
        /// <param name="value">The KeyValues3 value, or null.</param>
        /// <returns>The converted value.</returns>
        public static SmartPropValue FromKV(KVObject? value)
        {
            if (value == null)
            {
                return Null;
            }

            switch (value.ValueType)
            {
                case KVValueType.Boolean:
                    return FromBool((bool)value);
                case KVValueType.Int16:
                case KVValueType.Int32:
                case KVValueType.Int64:
                    return FromInt((long)value);
                case KVValueType.UInt16:
                case KVValueType.UInt32:
                case KVValueType.UInt64:
                    return new(SmartPropValueKind.Int, integer: unchecked((long)(ulong)value), isUnsigned: true);
                case KVValueType.FloatingPoint:
                case KVValueType.FloatingPoint64:
                    return FromFloat((double)value);
                case KVValueType.String:
                    return FromString((string)value);
                case KVValueType.Array:
                {
                    var elements = (IReadOnlyList<KVObject>)value.Values;
                    var array = new double[elements.Count];
                    var allIntegers = true;

                    for (var i = 0; i < array.Length; i++)
                    {
                        var element = FromKV(elements[i]);
                        allIntegers &= element.Kind == SmartPropValueKind.Int;
                        array[i] = element.Kind == SmartPropValueKind.Int ? element.integer : element.GetFloat();
                    }

                    return new(SmartPropValueKind.Array, items: array, hasIntegerItems: allIntegers && array.Length > 0);
                }
                default:
                    return Null;
            }
        }

        /// <summary>Array length, 0 for anything but an array.</summary>
        public int Count => items?.Length ?? 0;

        /// <summary>Gets an array element as a double, 0 when out of range.</summary>
        /// <param name="index">Element index.</param>
        /// <returns>The element.</returns>
        public double GetItem(int index) => items != null && index < items.Length ? items[index] : 0;

        /// <summary>
        /// Reads the value as a boolean: numbers are true when their integer part is nonzero, and strings accept
        /// <c>true</c>/<c>false</c>, <c>yes</c>/<c>no</c> or an integer.
        /// </summary>
        /// <returns>The converted value.</returns>
        public bool GetBool() => Kind switch
        {
            SmartPropValueKind.Bool => number != 0,
            SmartPropValueKind.Int => integer != 0,
            SmartPropValueKind.Float => TruncateToInt32(number) != 0,
            SmartPropValueKind.String => StringToBool(text!),
            _ => false,
        };

        /// <summary>
        /// Reads the value as a 32-bit integer: floats truncate, larger integers keep their low 32 bits and strings
        /// must hold a whole integer in range, otherwise they read as 0.
        /// </summary>
        /// <returns>The converted value.</returns>
        public int GetInt() => Kind switch
        {
            SmartPropValueKind.Bool => (int)number,
            SmartPropValueKind.Int => unchecked((int)integer),
            SmartPropValueKind.Float => TruncateToInt32(number),
            SmartPropValueKind.String => StringToInt(text!),
            _ => 0,
        };

        /// <summary>Reads the value as a float; strings must hold a whole number, otherwise they read as 0.</summary>
        /// <returns>The converted value.</returns>
        public float GetFloat() => Kind switch
        {
            SmartPropValueKind.Bool => (float)number,
            SmartPropValueKind.Int => unchecked((int)integer),
            SmartPropValueKind.Float => (float)number,
            SmartPropValueKind.String => (float)StringToFloat(text!),
            _ => 0f,
        };

#pragma warning disable CA1024 // Use properties where appropriate
        /// <summary>Reads the value as a string; anything but a string reads as an empty string.</summary>
        /// <returns>The converted value.</returns>
        public string GetString() => Kind == SmartPropValueKind.String ? text! : string.Empty;
#pragma warning restore CA1024

        /// <summary>
        /// Reads the value as a vector of <paramref name="count"/> components from an array or a space separated
        /// string of exactly that many numbers; anything else reads as a zero vector.
        /// </summary>
        /// <param name="count">Component count, 1 to 4.</param>
        /// <returns>The converted value, components past <paramref name="count"/> are 0.</returns>
        public Vector4 GetVector(int count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 4);

            Span<float> values = stackalloc float[4];

            return TryGetNumbers(values[..count], static item => (float)item, static part => (float)StringToFloat(part))
                ? new Vector4(values[0], values[1], values[2], values[3])
                : Vector4.Zero;
        }

        /// <summary>
        /// Reads the value as a color from four or three integers (opaque) in an array or a space separated string;
        /// anything else reads as opaque black.
        /// </summary>
        /// <returns>The converted value.</returns>
        public Color32 GetColor()
        {
            var isInteger = hasIntegerItems;
            Span<int> channels = stackalloc int[4];
            channels[3] = 255;

            int Item(double item) => isInteger ? unchecked((int)(long)item) : TruncateToInt32(item);

            if (!TryGetNumbers(channels, Item, StringToInt) && !TryGetNumbers(channels[..3], Item, StringToInt))
            {
                return new Color32(0, 0, 0, 255);
            }

            return new Color32(unchecked((byte)channels[0]), unchecked((byte)channels[1]), unchecked((byte)channels[2]), unchecked((byte)channels[3]));
        }

        private bool TryGetNumbers<T>(Span<T> values, Func<double, T> fromItem, Func<string, T> fromText)
        {
            if (Kind == SmartPropValueKind.Array)
            {
                if (items!.Length != values.Length)
                {
                    return false;
                }

                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = fromItem(items[i]);
                }

                return true;
            }

            if (Kind == SmartPropValueKind.String)
            {
                var parts = text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != values.Length)
                {
                    return false;
                }

                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = fromText(parts[i]);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// KeyValues3 equality: the same basic type (signed and unsigned integers compare as integers), doubles
        /// within 1e-7, strings case-sensitively, and arrays element by element, where integers never equal floats.
        /// </summary>
        /// <param name="other">Value to compare with.</param>
        /// <returns>Whether the values are equal.</returns>
        public bool IsEqual(SmartPropValue other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case SmartPropValueKind.Null:
                    return true;
                case SmartPropValueKind.Bool:
                    return number == other.number;
                case SmartPropValueKind.Int:
                    return integer == other.integer;
                case SmartPropValueKind.Float:
                    return Math.Abs(number - other.number) <= 1e-7;
                case SmartPropValueKind.String:
                    return string.Equals(text, other.text, StringComparison.Ordinal);
                default:
                    if (Count != other.Count)
                    {
                        return false;
                    }

                    if (hasIntegerItems != other.hasIntegerItems)
                    {
                        return Count == 0;
                    }

                    for (var i = 0; i < Count; i++)
                    {
                        var equal = hasIntegerItems ? items![i] == other.items![i] : Math.Abs(items![i] - other.items![i]) <= 1e-7;

                        if (!equal)
                        {
                            return false;
                        }
                    }

                    return true;
            }
        }

        /// <summary>
        /// KeyValues3 ordering: only values of the same scalar or string type are ordered (signed and unsigned
        /// integers are not), strings by their ASCII lower-case characters.
        /// </summary>
        /// <param name="other">Value to compare with.</param>
        /// <returns>Whether this value is greater.</returns>
        public bool IsGreater(SmartPropValue other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            return Kind switch
            {
                SmartPropValueKind.Bool => number != 0 && other.number == 0,
                SmartPropValueKind.Int when isUnsigned != other.isUnsigned => false,
                SmartPropValueKind.Int => isUnsigned ? (ulong)integer > (ulong)other.integer : integer > other.integer,
                SmartPropValueKind.Float => number > other.number,
                SmartPropValueKind.String => CompareAsciiIgnoreCase(text!, other.text!) > 0,
                _ => false,
            };
        }

        /// <inheritdoc/>
        public override string ToString() => Kind switch
        {
            SmartPropValueKind.Bool => number != 0 ? "true" : "false",
            SmartPropValueKind.Int => isUnsigned ? ((ulong)integer).ToString(CultureInfo.InvariantCulture) : integer.ToString(CultureInfo.InvariantCulture),
            SmartPropValueKind.Float => number.ToString(CultureInfo.InvariantCulture),
            SmartPropValueKind.String => text!,
            SmartPropValueKind.Array => string.Join(' ', items!.Select(static item => item.ToString(CultureInfo.InvariantCulture))),
            _ => string.Empty,
        };

        private static int CompareAsciiIgnoreCase(string a, string b)
        {
            static char Lower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

            for (var i = 0; ; i++)
            {
                var x = i < a.Length ? Lower(a[i]) : '\0';
                var y = i < b.Length ? Lower(b[i]) : '\0';

                if (x != y || x == '\0')
                {
                    return x - y;
                }
            }
        }

        /// <summary>
        /// Converts like a truncating x86 conversion: NaN and values outside the 32-bit range become <see cref="int.MinValue"/>.
        /// </summary>
        internal static int TruncateToInt32(double value) => value is > -2147483649.0 and < 2147483648.0 ? (int)value : int.MinValue;

        /// <summary>
        /// Parses a boolean: an empty string is false; <c>true</c>, <c>1</c> and <c>yes</c> are true and <c>false</c>,
        /// <c>0</c> and <c>no</c> false, case-insensitively; otherwise a whole decimal integer, true when nonzero.
        /// </summary>
        internal static bool StringToBool(string text)
        {
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (text.Length == 0 || text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0" || text.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryParseInt64(text, out var value) && value != 0;
        }

        /// <summary>
        /// Parses a whole decimal integer, with leading whitespace and a sign allowed; 0 when the text has anything
        /// else or the value is outside the 32-bit range.
        /// </summary>
        internal static int StringToInt(string text)
            => TryParseInt64(text, out var value) && value is >= int.MinValue and <= int.MaxValue ? (int)value : 0;

        /// <summary>
        /// Parses a whole decimal float, with leading whitespace and a leading <c>+</c> allowed, also <c>inf</c>,
        /// <c>infinity</c> and <c>nan</c>; 0 when the text has anything else.
        /// </summary>
        internal static double StringToFloat(string text)
        {
            var span = text.AsSpan().TrimStart(" \t\n\v\f\r");

            if (span.StartsWith('+'))
            {
                span = span[1..];
            }

            var negative = span.StartsWith('-');
            var unsigned = negative ? span[1..] : span;

            if (unsigned.Equals("inf", StringComparison.OrdinalIgnoreCase) || unsigned.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            {
                return negative ? double.NegativeInfinity : double.PositiveInfinity;
            }

            if (unsigned.StartsWith("nan", StringComparison.OrdinalIgnoreCase) && IsNanSuffix(unsigned[3..]))
            {
                return double.NaN;
            }

            var i = SkipDigits(unsigned, 0);
            var digits = i;

            if (i < unsigned.Length && unsigned[i] == '.')
            {
                var fraction = SkipDigits(unsigned, i + 1);
                digits += fraction - i - 1;
                i = fraction;
            }

            if (digits == 0)
            {
                return 0;
            }

            if (i < unsigned.Length && unsigned[i] is 'e' or 'E')
            {
                var exponent = i + 1;

                if (exponent < unsigned.Length && unsigned[exponent] is '+' or '-')
                {
                    exponent++;
                }

                var end = SkipDigits(unsigned, exponent);

                if (end > exponent)
                {
                    i = end;
                }
            }

            return i == unsigned.Length ? double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture) : 0;
        }

        private static bool IsNanSuffix(ReadOnlySpan<char> suffix)
        {
            if (suffix.IsEmpty)
            {
                return true;
            }

            if (suffix[0] != '(' || suffix[^1] != ')')
            {
                return false;
            }

            foreach (var c in suffix[1..^1])
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static int SkipDigits(ReadOnlySpan<char> text, int start)
        {
            while (start < text.Length && char.IsAsciiDigit(text[start]))
            {
                start++;
            }

            return start;
        }

        private static bool TryParseInt64(string text, out long value)
        {
            value = 0;
            var span = text.AsSpan().TrimStart(" \t\n\v\f\r");
            var negative = false;

            if (span.Length > 0 && span[0] is '+' or '-')
            {
                negative = span[0] == '-';
                span = span[1..];
            }

            if (span.IsEmpty)
            {
                return false;
            }

            var magnitude = 0UL;
            var overflow = false;

            foreach (var c in span)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }

                if (magnitude > (ulong.MaxValue - 9) / 10)
                {
                    overflow = true;
                }
                else
                {
                    magnitude = magnitude * 10 + (ulong)(c - '0');
                }
            }

            if (negative)
            {
                value = overflow || magnitude > 1UL << 63 ? long.MinValue : unchecked(-(long)magnitude);
            }
            else
            {
                value = overflow || magnitude > long.MaxValue ? long.MaxValue : (long)magnitude;
            }

            return true;
        }
    }
}
