using System.Diagnostics;

namespace GUI.Utils
{
    [DebuggerDisplay("R={R}, G={G}, B={B}, A={A} (#{string.Format(\"{0:X8}\", PackedValue),nq})")]
    public record struct Color32(uint PackedValue)
    {
        public static readonly Color32 White = new(0xFFFFFFFF);

        public Color32(byte r, byte g, byte b) : this(r, g, b, 255) { }

        public Color32(byte r, byte g, byte b, byte a) : this(0)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Color32(float r, float g, float b, float a) : this((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)(a * 255))
        {
            Debug.Assert(
                r >= 0.0f && r <= 1.0f && g >= 0.0f && g <= 1.0f && b >= 0.0f && b <= 1.0f && a >= 0.0f && a <= 1.0f,
                "Float components must be in the range [0, 1]. A missed implicit cast perhaps?"
            );
        }

        public byte R { readonly get => (byte)(PackedValue >> 0); set => PackedValue = (PackedValue & 0xFFFFFF00) | ((uint)value << 0); }
        public byte G { readonly get => (byte)(PackedValue >> 8); set => PackedValue = (PackedValue & 0xFFFF00FF) | ((uint)value << 8); }
        public byte B { readonly get => (byte)(PackedValue >> 16); set => PackedValue = (PackedValue & 0xFF00FFFF) | ((uint)value << 16); }
        public byte A { readonly get => (byte)(PackedValue >> 24); set => PackedValue = (PackedValue & 0x00FFFFFF) | ((uint)value << 24); }
    }
}
