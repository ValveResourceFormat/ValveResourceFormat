namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Converts colours in 0 to 1 between RGB and HSV, with hue in degrees.
    /// </summary>
    internal static class HsvColor
    {
        /// <summary>Converts an RGB colour to (hue in degrees, saturation, value). A grey has hue 0.</summary>
        public static Vector3 FromRgb(Vector3 rgb)
        {
            var max = rgb.MaxComponent();
            var chroma = max - rgb.MinComponent();
            var saturation = max == 0f ? 0f : chroma / max;

            if (saturation == 0f)
            {
                return new Vector3(0f, saturation, max);
            }

            var sextant = rgb.X == max ? (rgb.Y - rgb.Z) / chroma
                : rgb.Y == max ? ((rgb.Z - rgb.X) / chroma) + 2f
                : ((rgb.X - rgb.Y) / chroma) + 4f;

            var hue = sextant * 60f;

            if (hue < 0f)
            {
                hue += 360f;
            }

            return new Vector3(hue, saturation, max);
        }

        /// <summary>Converts (hue in degrees from 0 to 360, saturation, value) to an RGB colour.</summary>
        public static Vector3 ToRgb(Vector3 hsv)
        {
            var c = hsv.Z * hsv.Y;
            var h = hsv.X / 60f;
            var x = c * (1f - MathF.Abs((h % 2f) - 1f));

            return (int)h switch
            {
                0 or 6 => new Vector3(c, x, 0f),
                1 => new Vector3(x, c, 0f),
                2 => new Vector3(0f, c, x),
                3 => new Vector3(0f, x, c),
                4 => new Vector3(x, 0f, c),
                _ => new Vector3(c, 0f, x),
            } + new Vector3(hsv.Z - c);
        }
    }
}
