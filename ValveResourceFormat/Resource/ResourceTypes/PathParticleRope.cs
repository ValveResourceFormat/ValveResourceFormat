using System.Buffers;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// A single node of a <c>path_particle_rope</c> spline: a position plus the incoming and
    /// outgoing cubic tangent handles authored in Hammer. The spline segment between node i and
    /// node i+1 is the cubic Bezier with control points
    /// <c>(pos_i, pos_i + OutTangent_i, pos_{i+1} + InTangent_{i+1}, pos_{i+1})</c>.
    /// </summary>
    public readonly struct PathParticleRopeNode
    {
        /// <summary>Origin-relative node position.</summary>
        public Vector3 Position { get; init; }
        /// <summary>Incoming tangent handle (relative offset from <see cref="Position"/>).</summary>
        public Vector3 InTangent { get; init; }
        /// <summary>Outgoing tangent handle (relative offset from <see cref="Position"/>).</summary>
        public Vector3 OutTangent { get; init; }
    }

    /// <summary>
    /// Parsers for the KeyValues3 text values stored as strings on
    /// <c>path_particle_rope</c> / <c>path_particle_rope_clientside</c> entities
    /// (<c>pathnodes</c>, <c>pathnoderadiusscales</c>, <c>pathnodecolors</c>, <c>pathnodepinsenabled</c>).
    /// The values look like <c>"[ [ 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 2.0, 3.0, ], [ ... ], ]"</c>.
    /// </summary>
    /// <remarks>
    /// The keys hold KV3 text arrays, not keyvalue strings, so the keyvalue text parsers do not read them. Older data
    /// without KV3 syntax is read as plain numbers, where brackets, commas and spaces only separate them.
    /// </remarks>
    public static class PathParticleRope
    {
        /// <summary>Floats per <c>pathnodes</c> entry: position(3) + inTangent(3) + outTangent(3).</summary>
        public const int FloatsPerNode = 9;

        private const int FloatsPerColor = 3;

        private static readonly SearchValues<char> Separators = SearchValues.Create("[],\" \t\r\n\f\v");

        /// <summary>
        /// Parses the <c>pathnodes</c> value into a list of spline nodes. Each node reads exactly 9 floats,
        /// zero-padding a short entry. Returns an empty list when the text is empty.
        /// </summary>
        public static List<PathParticleRopeNode> ParseNodes(string? pathNodes)
        {
            var elements = ParseArray(pathNodes, nested: true);

            if (elements == null)
            {
                return ParseFlatNodes(pathNodes);
            }

            var nodes = new List<PathParticleRopeNode>(elements.Count);

            Span<float> floats = stackalloc float[FloatsPerNode];

            foreach (var element in elements)
            {
                floats.Clear();

                var count = Math.Min(element.Count, floats.Length);

                for (var i = 0; i < count; i++)
                {
                    floats[i] = element[i].ToFloatOrDefault();
                }

                nodes.Add(CreateNode(floats));
            }

            return nodes;
        }

        // Older data writes the numbers without KV3 syntax, so the brackets, commas and spaces only separate them
        private static List<PathParticleRopeNode> ParseFlatNodes(string? text)
        {
            var floats = ParseFlatFloats(text);
            var nodeCount = floats.Length / FloatsPerNode;
            var nodes = new List<PathParticleRopeNode>(nodeCount);

            for (var i = 0; i < nodeCount; i++)
            {
                var offset = i * FloatsPerNode;

                nodes.Add(CreateNode(floats.AsSpan(offset, FloatsPerNode)));
            }

            return nodes;
        }

        private static PathParticleRopeNode CreateNode(ReadOnlySpan<float> floats) => new()
        {
            Position = new Vector3(floats[0..3]),
            InTangent = new Vector3(floats[3..6]),
            OutTangent = new Vector3(floats[6..9]),
        };

        private static float[] ParseFlatFloats(string? text)
        {
            if (text == null)
            {
                return [];
            }

            var result = new List<float>();
            var span = text.AsSpan();

            foreach (var range in span.SplitAny(Separators))
            {
                var token = span[range].Trim();

                if (token.IsEmpty)
                {
                    continue;
                }

                // A token that is not a number reads as 0 so the later fields stay aligned
                result.Add(ValueParsing.ParseFloat(token));
            }

            return [.. result];
        }

        /// <summary>
        /// Parses the <c>pathnoderadiusscales</c> value into <paramref name="nodeCount"/> per-node radius multipliers.
        /// Missing entries are 1.
        /// </summary>
        public static float[] ParseRadiusScales(string? input, int nodeCount) => ParseFloats(input, nodeCount, 1f);

        /// <summary>
        /// Parses a per-node array of floats such as <c>pathnodemovespeedtypes</c> into <paramref name="nodeCount"/> values.
        /// Missing entries are <paramref name="defaultValue"/>.
        /// </summary>
        public static float[] ParseFloats(string? input, int nodeCount, float defaultValue)
        {
            var elements = ParseArray(input);
            var source = elements == null ? ParseFlatFloats(input) : [.. elements.Select(static element => element.ToFloatOrDefault())];
            var values = new float[nodeCount];

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = i < source.Length ? source[i] : defaultValue;
            }

            return values;
        }

        /// <summary>
        /// Parses the <c>pathnodecolors</c> value (<c>[[r,g,b],...]</c>, components in 0-1) into <paramref name="nodeCount"/> per-node colors.
        /// An entry that is not three components is black, and missing entries are white.
        /// </summary>
        public static Vector3[] ParseColors(string? input, int nodeCount)
        {
            var elements = ParseArray(input, nested: true);

            if (elements == null)
            {
                return ParseFlatColors(input, nodeCount);
            }

            var colors = new Vector3[nodeCount];

            for (var i = 0; i < colors.Length; i++)
            {
                if (i >= elements.Count)
                {
                    colors[i] = Vector3.One;
                    continue;
                }

                var element = elements[i];

                if (element.Count == FloatsPerColor)
                {
                    colors[i] = new Vector3(element[0].ToFloatOrDefault(), element[1].ToFloatOrDefault(), element[2].ToFloatOrDefault());
                }
            }

            return colors;
        }

        /// <summary>
        /// Parses the <c>pathnodepinsenabled</c> value (<c>[true, false, ...]</c>) into <paramref name="nodeCount"/> booleans.
        /// Missing entries are true.
        /// </summary>
        public static bool[] ParsePins(string? input, int nodeCount)
        {
            var elements = ParseArray(input);
            var pins = new bool[nodeCount];

            if (elements == null)
            {
                // Pins are sometimes written as 1.0 or 0.0, and unknown text stays pinned so the nodes stay aligned
                Array.Fill(pins, true);

                if (input == null)
                {
                    return pins;
                }

                var text = input.AsSpan();
                var index = 0;

                foreach (var range in text.SplitAny(Separators))
                {
                    var token = text[range].Trim();

                    if (token.IsEmpty)
                    {
                        continue;
                    }

                    if (index == pins.Length)
                    {
                        break;
                    }

                    pins[index] = ValueParsing.ParseBool(token, true);
                    index++;
                }

                return pins;
            }

            for (var i = 0; i < pins.Length; i++)
            {
                pins[i] = i >= elements.Count || elements[i].ToBooleanOrDefault();
            }

            return pins;
        }

        private static Vector3[] ParseFlatColors(string? text, int nodeCount)
        {
            var floats = ParseFlatFloats(text);
            var colors = new Vector3[nodeCount];

            for (var i = 0; i < colors.Length; i++)
            {
                var offset = i * FloatsPerColor;
                colors[i] = offset + FloatsPerColor <= floats.Length ? new Vector3(floats.AsSpan(offset)) : Vector3.One;
            }

            return colors;
        }

        // Null when the text is not a KV3 array, or when nested arrays are expected and an element is neither an
        // array nor null, so the caller reads the text as plain numbers instead
        private static IReadOnlyList<KVObject>? ParseArray(string? text, bool nested = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            if (!KVDocumentExtensions.TryParseKV3WithoutHeader(text, out var root, out _) || !root.IsArray)
            {
                return null;
            }

            if (nested && root.Elements.Any(element => !element.IsArray && !element.IsNull))
            {
                return null;
            }

            return root.Elements;
        }
    }
}
