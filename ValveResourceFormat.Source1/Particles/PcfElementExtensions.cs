using Datamodel;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>Reads the settings of a function or a system with the defaults the game uses for a missing or differently typed value.</summary>
internal static class PcfElementExtensions
{
    extension(Element element)
    {
        /// <summary>Gets an attribute by name, ignoring case, as files spell the names of settings differently.</summary>
        public bool TryGetAttribute(string name, out object? value)
        {
            if (element.TryGetValue(name, out value))
            {
                return true;
            }

            foreach (var (key, attribute) in element)
            {
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = attribute;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public bool HasAttribute(string name) => element.TryGetAttribute(name, out _);

        public float GetFloat(string name, float defaultValue = 0f)
        {
            if (!element.TryGetAttribute(name, out var value))
            {
                return defaultValue;
            }

            return value switch
            {
                float number => number,
                int number => number,
                byte number => number,
                ulong number => number,
                bool flag => flag ? 1f : 0f,
                TimeSpan time => (float)time.TotalSeconds,
                _ => defaultValue,
            };
        }

        public int GetInt(string name, int defaultValue = 0)
        {
            if (!element.TryGetAttribute(name, out var value))
            {
                return defaultValue;
            }

            return value switch
            {
                int number => number,
                float number => (int)number,
                byte number => number,
                ulong number => (int)number,
                bool flag => flag ? 1 : 0,
                _ => defaultValue,
            };
        }

        public bool GetBool(string name, bool defaultValue = false)
        {
            if (!element.TryGetAttribute(name, out var value))
            {
                return defaultValue;
            }

            return value switch
            {
                bool flag => flag,
                int number => number != 0,
                float number => number != 0f,
                _ => defaultValue,
            };
        }

        public string GetString(string name, string defaultValue = "")
            => element.TryGetAttribute(name, out var value) && value is string text ? text : defaultValue;

        public Vector3 GetVector3(string name, Vector3 defaultValue = default)
            => element.TryGetAttribute(name, out var value) && value is Vector3 vector ? vector : defaultValue;

        public Color GetColor(string name, Color defaultValue)
            => element.TryGetAttribute(name, out var value) && value is Color color ? color : defaultValue;

        public List<Element> GetElements(string name)
        {
            var elements = new List<Element>();

            if (!element.TryGetAttribute(name, out var value) || value is not IEnumerable<Element> list)
            {
                return elements;
            }

            foreach (var item in list)
            {
                if (item != null && !item.Stub)
                {
                    elements.Add(item);
                }
            }

            return elements;
        }
    }
}
