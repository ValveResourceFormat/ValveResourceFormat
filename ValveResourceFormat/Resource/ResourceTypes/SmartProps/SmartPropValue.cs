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
        /// <summary>Signed integer.</summary>
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
    /// A KeyValues3-like value held by a smart prop variable.
    /// </summary>
    public readonly struct SmartPropValue
    {
        /// <summary>The null value.</summary>
        public static SmartPropValue Null => default;

        /// <summary>Basic type of the value.</summary>
        public SmartPropValueKind Kind { get; }

        private readonly double number;
        private readonly string? text;
        private readonly double[]? items;

        private SmartPropValue(SmartPropValueKind kind, double number, string? text, double[]? items)
        {
            Kind = kind;
            this.number = number;
            this.text = text;
            this.items = items;
        }

        /// <summary>Creates a boolean value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromBool(bool value) => new(SmartPropValueKind.Bool, value ? 1 : 0, null, null);

        /// <summary>Creates an integer value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromInt(long value) => new(SmartPropValueKind.Int, value, null, null);

        /// <summary>Creates a float value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromFloat(double value) => new(SmartPropValueKind.Float, value, null, null);

        /// <summary>Creates a string value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromString(string value) => new(SmartPropValueKind.String, 0, value, null);

        /// <summary>Creates an array value.</summary>
        /// <param name="values">The array elements.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromArray(params double[] values) => new(SmartPropValueKind.Array, 0, null, values);

        /// <summary>Creates a three component vector value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromVector(Vector3 value) => FromArray(value.X, value.Y, value.Z);

        /// <summary>Creates a color value, with three components when fully opaque.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The created value.</returns>
        public static SmartPropValue FromColor(Color32 value) => value.A == 255
            ? FromArray(value.R, value.G, value.B)
            : FromArray(value.R, value.G, value.B, value.A);

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
                case KVValueType.UInt16:
                case KVValueType.UInt32:
                case KVValueType.UInt64:
                    return FromInt((long)value);
                case KVValueType.FloatingPoint:
                case KVValueType.FloatingPoint64:
                    return FromFloat((double)value);
                case KVValueType.String:
                    return FromString((string)value);
                case KVValueType.Array:
                {
                    var elements = (IReadOnlyList<KVObject>)value.Values;
                    var array = new double[elements.Count];

                    for (var i = 0; i < array.Length; i++)
                    {
                        array[i] = FromKV(elements[i]).GetFloat();
                    }

                    return FromArray(array);
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

        /// <summary>Reads the value as a boolean.</summary>
        /// <returns>The converted value.</returns>
        public bool GetBool() => Kind switch
        {
            SmartPropValueKind.Bool or SmartPropValueKind.Int or SmartPropValueKind.Float => number != 0,
            SmartPropValueKind.String => text!.Equals("true", StringComparison.OrdinalIgnoreCase) || StringToInt(text) != 0,
            _ => false,
        };

        /// <summary>Reads the value as an integer: floats truncate, strings parse like <c>atoi</c>.</summary>
        /// <returns>The converted value.</returns>
        public int GetInt() => Kind switch
        {
            SmartPropValueKind.Bool or SmartPropValueKind.Int => (int)(long)number,
            SmartPropValueKind.Float => (int)number,
            SmartPropValueKind.String => StringToInt(text!),
            _ => 0,
        };

        /// <summary>Reads the value as a float; strings parse like <c>atof</c>.</summary>
        /// <returns>The converted value.</returns>
        public float GetFloat() => (float)GetDouble();

        private double GetDouble() => Kind switch
        {
            SmartPropValueKind.Bool or SmartPropValueKind.Int or SmartPropValueKind.Float => number,
            SmartPropValueKind.String => StringToFloat(text!),
            _ => 0,
        };

        /// <summary>Reads the value as a string.</summary>
        /// <returns>The converted value.</returns>
        public string GetString() => Kind switch
        {
            SmartPropValueKind.String => text!,
            SmartPropValueKind.Bool => number != 0 ? "true" : "false",
            SmartPropValueKind.Int => ((long)number).ToString(CultureInfo.InvariantCulture),
            SmartPropValueKind.Float => number.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

        /// <summary>Reads the value as a vector of up to four components; missing components are 0.</summary>
        /// <returns>The converted value.</returns>
        public Vector4 GetVector() => new((float)GetItem(0), (float)GetItem(1), (float)GetItem(2), (float)GetItem(3));

        /// <summary>Reads the value as a color; three element arrays are opaque.</summary>
        /// <returns>The converted value.</returns>
        public Color32 GetColor()
        {
            if (Kind != SmartPropValueKind.Array)
            {
                return new Color32(0, 0, 0, 255);
            }

            static byte Channel(double value) => unchecked((byte)(int)value);

            return new Color32(Channel(GetItem(0)), Channel(GetItem(1)), Channel(GetItem(2)), Count >= 4 ? Channel(GetItem(3)) : (byte)255);
        }

        /// <summary>
        /// KeyValues3 equality: the same basic type (integers compare as integers), doubles within 1e-7,
        /// strings case-sensitively.
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
                case SmartPropValueKind.Int:
                    return number == other.number;
                case SmartPropValueKind.Float:
                    return Math.Abs(number - other.number) <= 1e-7;
                case SmartPropValueKind.String:
                    return string.Equals(text, other.text, StringComparison.Ordinal);
                default:
                    if (Count != other.Count)
                    {
                        return false;
                    }

                    for (var i = 0; i < Count; i++)
                    {
                        if (Math.Abs(items![i] - other.items![i]) > 1e-7)
                        {
                            return false;
                        }
                    }

                    return true;
            }
        }

        /// <summary>
        /// KeyValues3 ordering: only values of the same scalar or string type are ordered, strings case-insensitively.
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
                SmartPropValueKind.Bool or SmartPropValueKind.Int or SmartPropValueKind.Float => number > other.number,
                SmartPropValueKind.String => string.Compare(text, other.text, StringComparison.OrdinalIgnoreCase) > 0,
                _ => false,
            };
        }

        /// <inheritdoc/>
        public override string ToString() => Kind == SmartPropValueKind.Array
            ? string.Join(' ', items!.Select(static item => item.ToString(CultureInfo.InvariantCulture)))
            : GetString();

        /// <summary>
        /// Parses a leading integer like <c>atoi</c>: optional sign, decimal digits up to the first other character.
        /// </summary>
        /// <param name="text">Text to parse.</param>
        /// <returns>The parsed value, 0 when there are no digits.</returns>
        public static int StringToInt(string text)
        {
            var span = text.AsSpan().TrimStart();
            var negative = false;

            if (span.Length > 0 && (span[0] == '-' || span[0] == '+'))
            {
                negative = span[0] == '-';
                span = span[1..];
            }

            long value = 0;

            foreach (var c in span)
            {
                if (!char.IsAsciiDigit(c))
                {
                    break;
                }

                value = Math.Min(value * 10 + (c - '0'), 1L << 32);
            }

            return unchecked((int)(negative ? -value : value));
        }

        /// <summary>
        /// Parses a leading float like <c>atof</c>, ignoring trailing characters.
        /// </summary>
        /// <param name="text">Text to parse.</param>
        /// <returns>The parsed value, 0 when nothing parses.</returns>
        public static double StringToFloat(string text)
        {
            var span = text.AsSpan().Trim();

            for (var length = span.Length; length > 0; length--)
            {
                if (double.TryParse(span[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return 0;
        }
    }
}
