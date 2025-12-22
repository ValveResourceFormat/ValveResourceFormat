using SkiaSharp;

namespace NodeGraphControl
{
    public static class SharedState
    {
        public const int CornerSize = 12;

        public static byte NodeColorAlpha { get; set; } = 235;

        public static SKColor DefaultTypeColor { get; set; } = SKColors.Fuchsia;

        public static readonly Dictionary<Type, SKColor> TypeColor = [];

        public static SKColor GetColorByType(Type type)
        {
            TypeColor.TryGetValue(type, out var color);
            return (color != SKColor.Empty) ? color : DefaultTypeColor;
        }

        // todo wire style
    }
}
