namespace ValveResourceFormat.Source1.Bsp
{
    /// <summary>
    /// The brightness of every light style of a map when it starts, as a fraction of the light baked into the lightmaps.
    /// A face stores up to four lightmaps, one per light style, and the engine adds them scaled by the current
    /// brightness of their style.
    /// </summary>
    /// <remarks>
    /// A style is a pattern of letters from <c>a</c> (dark) to <c>z</c> (twice as bright as <c>m</c>, which is normal),
    /// one letter per tenth of a second. An animated style is represented by the average of its pattern.
    /// </remarks>
    internal static class BspLightStyles
    {
        /// <summary>The style stored in the unused slots of a face.</summary>
        public const byte None = 255;

        /// <summary>The first style that lights assign to themselves, so that they can be switched.</summary>
        public const int FirstSwitchableStyle = 32;

        private const int StyleCount = 64;
        private const int StartDisabledFlag = 1;
        private const char DarkLetter = 'a';
        private const char NormalLetter = 'm';

        // Styles 0 to 12 are the same in every map
        private static readonly string[] DefaultPatterns =
        [
            "m",
            "mmnmmommommnonmmonqnmmo",
            "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
            "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
            "mamamamamama",
            "jklmnopqrstuvwxyzyxwvutsrqponmlkj",
            "nmonqnmomnmomomno",
            "mmmaaaabcdefgmmmmaaaammmaamm",
            "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
            "aaaaaaaazzzzzzzz",
            "mmamammmmammamamaaamammma",
            "abcdefghijklmnopqrrqponmlkjihgfedcba",
            "mmnnmmnnnmmnn",
        ];

        /// <summary>Gets the brightness of every possible style value, indexed by the style stored in a face.</summary>
        /// <param name="entities">The entities of the map, whose lights set the styles they use.</param>
        public static float[] GetDefaultBrightness(IReadOnlyList<BspEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);

            // A style that nothing sets is a little darker than normal
            var brightness = new float[byte.MaxValue + 1];
            Array.Fill(brightness, 256f / 264f);

            for (var style = 0; style < DefaultPatterns.Length; style++)
            {
                brightness[style] = GetPatternBrightness(DefaultPatterns[style]);
            }

            brightness[StyleCount - 1] = 0f;

            // The last light to set a style wins, as lights that share a style set the same one
            foreach (var entity in entities)
            {
                if (!IsLight(entity.ClassName))
                {
                    continue;
                }

                var style = entity.GetInt("style");

                if (style < FirstSwitchableStyle || style >= StyleCount - 1)
                {
                    continue;
                }

                if ((entity.GetInt("spawnflags") & StartDisabledFlag) != 0)
                {
                    brightness[style] = 0f;
                    continue;
                }

                var pattern = entity.GetString("pattern");

                if (pattern.Length == 0)
                {
                    pattern = GetDefaultPattern(entity.GetInt("defaultstyle"));
                }

                brightness[style] = GetPatternBrightness(pattern);
            }

            return brightness;
        }

        private static bool IsLight(string className)
            => className is "light" or "light_spot" or "light_environment";

        /// <summary>Gets the pattern of one of the styles every map has, or the normal pattern for any other number.</summary>
        /// <param name="style">The style number.</param>
        public static string GetDefaultPattern(int style)
        {
            if (style > 0 && style < DefaultPatterns.Length)
            {
                return DefaultPatterns[style];
            }

            return DefaultPatterns[0];
        }

        private static float GetPatternBrightness(string pattern)
        {
            var sum = 0f;

            foreach (var letter in pattern)
            {
                sum += letter - DarkLetter;
            }

            return sum / pattern.Length / (NormalLetter - DarkLetter);
        }
    }
}
