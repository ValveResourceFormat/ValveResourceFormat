using System.Globalization;
using System.Text;

namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class BspEntity
    {
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<KeyValuePair<string, string>> AllProperties { get; } = [];

        public string ClassName => GetString("classname");
        public string TargetName => GetString("targetname");
        public Vector3 Origin => GetVector3("origin");
        public Vector3 Angles => GetVector3("angles");

        public string GetString(string key, string defaultValue = "")
        {
            return Properties.GetValueOrDefault(key, defaultValue);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (!Properties.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return intValue;
            }

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)
                ? (int)floatValue
                : defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (!Properties.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            return float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
        }

        public Vector3 GetVector3(string key)
        {
            if (!Properties.TryGetValue(key, out var value))
            {
                return Vector3.Zero;
            }

            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var x = ParseComponent(parts, 0);
            var y = ParseComponent(parts, 1);
            var z = ParseComponent(parts, 2);
            return new Vector3(x, y, z);
        }

        private static float ParseComponent(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return 0f;
            }

            return float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0f;
        }

        public override string ToString() => ClassName;

        internal static List<BspEntity> Parse(ReadOnlySpan<byte> lump)
        {
            var text = Encoding.Latin1.GetString(lump);
            var result = new List<BspEntity>();

            BspEntity? current = null;
            string? pendingKey = null;
            var position = 0;

            while (position < text.Length)
            {
                var c = text[position];

                if (c == '\0')
                {
                    break;
                }

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

                if (c == '{')
                {
                    current = new BspEntity();
                    pendingKey = null;
                    position++;
                    continue;
                }

                if (c == '}')
                {
                    if (current != null)
                    {
                        result.Add(current);
                    }

                    current = null;
                    pendingKey = null;
                    position++;
                    continue;
                }

                if (c != '"')
                {
                    position++;
                    continue;
                }

                var end = text.IndexOf('"', position + 1);

                if (end < 0)
                {
                    break;
                }

                var token = text.Substring(position + 1, end - position - 1);
                position = end + 1;

                if (current == null)
                {
                    continue;
                }

                if (pendingKey == null)
                {
                    pendingKey = token;
                    continue;
                }

                current.AllProperties.Add(new KeyValuePair<string, string>(pendingKey, token));
                current.Properties[pendingKey] = token;
                pendingKey = null;
            }

            return result;
        }
    }
}
