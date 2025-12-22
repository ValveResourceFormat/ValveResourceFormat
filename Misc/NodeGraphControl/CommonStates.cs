using System.Drawing;

namespace NodeGraphControl
{
    public static class CommonStates
    {
        public const int CornerSize = 12;

        public static byte NodeColorAlpha { get; set; } = 235;

        public static Color DefaultTypeColor { get; set; } = Color.Fuchsia;

        public static readonly Dictionary<Type, Color> TypeColor = [];

        public static Color GetColorByType(Type type)
        {
            TypeColor.TryGetValue(type, out var color);
            return (!color.IsEmpty) ? color : DefaultTypeColor;
        }

        // todo wire style
    }
}
