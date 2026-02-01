using System.Diagnostics;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// 32-bit RGBA color with byte component storage.
    /// </summary>
    [DebuggerDisplay("R={R}, G={G}, B={B}, A={A} ({HexCode,nq})")]
    public record struct Color32(uint PackedValue)
    {
        /// <summary>
        /// White color (255, 255, 255, 255).
        /// </summary>
        public static readonly Color32 White = new(0xFFFFFFFF);

        /// <summary>
        /// Red color (255, 0, 0, 255).
        /// </summary>
        public static readonly Color32 Red = new(1f, 0f, 0f, 1f);

        /// <summary>
        /// Green color (0, 255, 0, 255).
        /// </summary>
        public static readonly Color32 Green = new(0f, 1f, 0f, 1f);

        /// <summary>
        /// Blue color (0, 0, 255, 255).
        /// </summary>
        public static readonly Color32 Blue = new(0f, 0f, 1f, 1f);

        /// <summary>
        /// Black color (0, 0, 0, 255).
        /// </summary>
        public static readonly Color32 Black = new(0f, 0f, 0f, 1f);

        /// <summary>
        /// Yellow color (255, 255, 0, 255).
        /// </summary>
        public static readonly Color32 Yellow = new(1f, 1f, 0f, 1f);

        /// <summary>
        /// Initializes a new color from RGB byte components with full opacity.
        /// </summary>
        /// <param name="r">Red component (0-255).</param>
        /// <param name="g">Green component (0-255).</param>
        /// <param name="b">Blue component (0-255).</param>
        public Color32(byte r, byte g, byte b) : this(r, g, b, 255) { }

        /// <summary>
        /// Initializes a new color from RGBA byte components.
        /// </summary>
        /// <param name="r">Red component (0-255).</param>
        /// <param name="g">Green component (0-255).</param>
        /// <param name="b">Blue component (0-255).</param>
        /// <param name="a">Alpha component (0-255).</param>
        public Color32(byte r, byte g, byte b, byte a) : this(0)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>
        /// Initializes a new color from RGBA float components.
        /// </summary>
        /// <param name="r">Red component (0.0-1.0).</param>
        /// <param name="g">Green component (0.0-1.0).</param>
        /// <param name="b">Blue component (0.0-1.0).</param>
        /// <param name="a">Alpha component (0.0-1.0).</param>
        public Color32(float r, float g, float b, float a) : this((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)(a * 255))
        {
            Debug.Assert(
                r >= 0.0f && r <= 1.0f && g >= 0.0f && g <= 1.0f && b >= 0.0f && b <= 1.0f && a >= 0.0f && a <= 1.0f,
                "Float components must be in the range [0, 1]. A missed implicit cast perhaps?"
            );
        }

        /// <summary>
        /// Creates a color from a <see cref="Vector4"/> where XYZW maps to RGBA.
        /// </summary>
        /// <param name="vector">Vector with color components (0.0-1.0).</param>
        /// <returns>The converted color.</returns>
        public static Color32 FromVector4(Vector4 vector) => new(vector.X, vector.Y, vector.Z, vector.W);

        /// <summary>
        /// Gets the color as an 8-character hex string in #RRGGBBAA format.
        /// </summary>
        public readonly string HexCode => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

        /// <summary>
        /// Gets or sets the red component (0-255).
        /// </summary>
        public byte R { readonly get => (byte)(PackedValue >> 0); set => PackedValue = (PackedValue & 0xFFFFFF00) | ((uint)value << 0); }

        /// <summary>
        /// Gets or sets the green component (0-255).
        /// </summary>
        public byte G { readonly get => (byte)(PackedValue >> 8); set => PackedValue = (PackedValue & 0xFFFF00FF) | ((uint)value << 8); }

        /// <summary>
        /// Gets or sets the blue component (0-255).
        /// </summary>
        public byte B { readonly get => (byte)(PackedValue >> 16); set => PackedValue = (PackedValue & 0xFF00FFFF) | ((uint)value << 16); }

        /// <summary>
        /// Gets or sets the alpha component (0-255).
        /// </summary>
        public byte A { readonly get => (byte)(PackedValue >> 24); set => PackedValue = (PackedValue & 0x00FFFFFF) | ((uint)value << 24); }
    }
}
