using System;
using System.Collections.Generic;
using System.Drawing;

namespace NodeGraphControl {
    public static class CommonStates {
        
        // constants    //////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////
        
        public const int CornerSize = 12;
        
        // states       //////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////
        
        public static bool SocketCaptionTypeToggle = false;

        // settings     //////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////
        
        public static byte NodeColorAlpha { get; set; } = 235;

        public static Color DefaultTypeColor { get; set; } = Color.Fuchsia;

        public static readonly Dictionary<Type, Color> TypeColor = new Dictionary<Type, Color>();

        public static Color GetColorByType(Type type) {
            Color color;
            TypeColor.TryGetValue(type, out color);
            return (!color.IsEmpty) ? color : DefaultTypeColor;
        }

        // todo wire style
    }
}