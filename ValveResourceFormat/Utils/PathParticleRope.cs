using System.Globalization;

namespace ValveResourceFormat.Utils
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
    /// Parsers for the bracketed numeric blobs stored as strings on
    /// <c>path_particle_rope</c> / <c>path_particle_rope_clientside</c> entities
    /// (<c>pathnodes</c>, <c>pathnoderadiusscales</c>, <c>pathnodecolors</c>, <c>pathnodepinsenabled</c>).
    /// The blobs look like <c>"[ [0,0,0, 0,0,0, 1,2,3], [...] ]"</c>; nesting is purely cosmetic so
    /// every parser flattens the brackets and reads the numbers in order.
    /// </summary>
    public static class PathParticleRope
    {
        /// <summary>Floats per <c>pathnodes</c> entry: position(3) + inTangent(3) + outTangent(3).</summary>
        public const int FloatsPerNode = 9;

        private static readonly char[] SplitChars = ['[', ']', ',', ' ', '\t', '\r', '\n', '\f', '\v'];

        /// <summary>
        /// Flattens a bracketed blob to a flat array of floats, ignoring all bracket nesting and whitespace.
        /// </summary>
        public static float[] ParseFloatBlob(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return [];
            }

            var tokens = input.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new List<float>(tokens.Length);

            foreach (var token in tokens)
            {
                // Keep the flat array aligned with the fixed-size node groups: substitute 0 for an unparseable
                // token instead of dropping it, which would shift every later field.
                result.Add(float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f);
            }

            return [.. result];
        }

        /// <summary>
        /// Parses the <c>pathnodes</c> blob into a list of spline nodes (groups of 9 floats).
        /// Returns an empty list for empty/degenerate input.
        /// </summary>
        public static List<PathParticleRopeNode> ParseNodes(string? pathNodes)
        {
            var floats = ParseFloatBlob(pathNodes);
            var nodeCount = floats.Length / FloatsPerNode;
            var nodes = new List<PathParticleRopeNode>(nodeCount);

            for (var i = 0; i < nodeCount; i++)
            {
                var o = i * FloatsPerNode;
                nodes.Add(new PathParticleRopeNode
                {
                    Position = new Vector3(floats[o + 0], floats[o + 1], floats[o + 2]),
                    InTangent = new Vector3(floats[o + 3], floats[o + 4], floats[o + 5]),
                    OutTangent = new Vector3(floats[o + 6], floats[o + 7], floats[o + 8]),
                });
            }

            return nodes;
        }

        /// <summary>
        /// Parses the <c>pathnoderadiusscales</c> blob into a flat array of per-node radius multipliers.
        /// </summary>
        public static float[] ParseRadiusScales(string? input) => ParseFloatBlob(input);

        /// <summary>
        /// Parses the <c>pathnodecolors</c> blob (nested <c>[[r,g,b],...]</c>, components in 0-1) into per-node colors.
        /// </summary>
        public static Vector3[] ParseColors(string? input)
        {
            var floats = ParseFloatBlob(input);
            var count = floats.Length / 3;
            var colors = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                colors[i] = new Vector3(floats[i * 3 + 0], floats[i * 3 + 1], floats[i * 3 + 2]);
            }

            return colors;
        }

        /// <summary>
        /// Parses the <c>pathnodepinsenabled</c> blob (<c>[true, false, ...]</c>) into a flat array of booleans.
        /// </summary>
        public static bool[] ParsePins(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return [];
            }

            var tokens = input.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new List<bool>(tokens.Length);

            foreach (var token in tokens)
            {
                if (bool.TryParse(token, out var value))
                {
                    result.Add(value);
                }
                else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    // Pins are sometimes encoded numerically (1/0 or 1.0/0.0); any non-zero means pinned.
                    result.Add(number != 0f);
                }
                else
                {
                    // Unknown spelling: keep the array aligned with the node list by falling back to the
                    // default (every node pinned) rather than dropping the entry.
                    result.Add(true);
                }
            }

            return [.. result];
        }
    }
}
