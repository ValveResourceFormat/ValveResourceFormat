using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// A colour ramp over [0, 1], blended between stops in whole 8-bit steps per channel.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/CColorGradient">CColorGradient</seealso>
    sealed class ColorGradient
    {
        private const float CoincidentStopDistance = 1e-5f;

        private readonly float[] positions;
        private readonly Vector3[] colors;

        /// <summary>
        /// Reads the stops with their positions clamped to [0, 1] and sorted, keeping stops at the
        /// same position in file order.
        /// </summary>
        public ColorGradient(KVObject? gradient)
        {
            var stops = gradient?.GetArray("m_Stops") ?? [];

            var sorted = stops
                .Select(stop => (
                    Position: Math.Clamp(stop.ContainsKey("m_flPosition") ? stop.GetFloatProperty("m_flPosition") : 0f, 0f, 1f),
                    Color: stop.ContainsKey("m_Color") ? ReadColor(stop.GetArray<int>("m_Color")) : Vector3.Zero))
                .OrderBy(stop => stop.Position)
                .ToArray();

            positions = [.. sorted.Select(stop => stop.Position)];
            colors = [.. sorted.Select(stop => stop.Color)];
        }

        private static Vector3 ReadColor(int[] color) => new(color[0], color[1], color[2]);

        /// <summary>
        /// The colour at <paramref name="t"/>, normalized to [0, 1]. Positions outside the stops take
        /// the nearest end stop, and a gradient without stops is white.
        /// </summary>
        public Vector3 Evaluate(float t)
        {
            if (positions.Length == 0)
            {
                return Vector3.One;
            }

            var index = FindStop(t);

            if (index < 0)
            {
                return colors[0] / 255f;
            }

            if (index == positions.Length - 1)
            {
                return colors[index] / 255f;
            }

            var span = positions[index + 1] - positions[index];

            if (span < CoincidentStopDistance)
            {
                return colors[index] / 255f;
            }

            var blended = MathUtils.Lerp(colors[index], colors[index + 1], (t - positions[index]) / span);

            return new Vector3(MathF.Truncate(blended.X), MathF.Truncate(blended.Y), MathF.Truncate(blended.Z)) / 255f;
        }

        /// <summary>The stop matching <paramref name="t"/>, else the last stop before it, or -1 when it is before every stop.</summary>
        private int FindStop(float t)
        {
            var low = 0;
            var high = positions.Length - 1;

            while (low <= high)
            {
                var middle = (low + high) >> 1;

                if (t > positions[middle])
                {
                    low = middle + 1;
                }
                else if (positions[middle] > t)
                {
                    high = middle - 1;
                }
                else
                {
                    return middle;
                }
            }

            return high;
        }
    }
}
