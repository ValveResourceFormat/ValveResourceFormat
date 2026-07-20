using GUI.Utils;
using SkiaSharp;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Theme-resolved colors for the node graph renderer. Chrome colors come from <see cref="Themer"/>,
/// hue slots are explicit per-theme constants so the same <see cref="GraphHue"/> reads correctly
/// on both the dark and the light canvas.
/// </summary>
class GraphPalette
{
    public bool IsDark { get; }

    public SKColor Canvas { get; }
    public SKColor NodeBody { get; }
    public SKColor NodeOutline { get; }
    public SKColor HeaderText { get; }
    public SKColor HeaderTextDim { get; }
    public SKColor Text { get; }
    public SKColor TextDim { get; }
    public SKColor GridDot { get; }
    public SKColor Selection { get; }
    public SKColor SelectionSoft { get; }
    public SKColor Hover { get; }
    public SKColor Shadow { get; }
    public SKColor MessageText { get; }
    public SKColor WireUnderlay { get; }
    public SKColor SocketOutline { get; }

    private readonly SKColor[] signal;
    private readonly SKColor[] category;

    /// <summary>Bright variant used for sockets and wires.</summary>
    public SKColor Signal(GraphHue hue) => signal[(int)hue];

    /// <summary>Muted variant used for node header bands.</summary>
    public SKColor Category(GraphHue hue) => category[(int)hue];

    public static GraphPalette ForCurrentTheme()
        => new(Themer.CurrentTheme == Themer.AppTheme.Dark, Themer.CurrentThemeColors);

    public GraphPalette(bool isDark, Themer.ThemeColors chrome)
    {
        IsDark = isDark;

        Canvas = ToSK(chrome.AppMiddle);
        NodeBody = ToSK(chrome.AppSoft);
        Text = ToSK(chrome.Contrast);
        TextDim = ToSK(chrome.ContrastSoft);
        GridDot = ToSK(chrome.Border);
        Selection = ToSK(chrome.Accent);
        SelectionSoft = ToSK(chrome.Accent).WithAlpha(150);

        NodeOutline = isDark ? SKColors.White.WithAlpha(38) : SKColors.Black.WithAlpha(38);
        HeaderText = isDark ? new SKColor(233, 235, 239) : SKColors.Black;
        HeaderTextDim = isDark ? new SKColor(233, 235, 239, 200) : new SKColor(25, 27, 31);
        Hover = isDark ? SKColors.White.WithAlpha(110) : SKColors.Black.WithAlpha(110);
        Shadow = isDark ? SKColors.Black.WithAlpha(110) : SKColors.Black.WithAlpha(60);
        MessageText = isDark ? ToSK(chrome.Accent) : new SKColor(32, 96, 190);
        WireUnderlay = isDark ? SKColors.Black.WithAlpha(90) : SKColors.Black.WithAlpha(36);
        SocketOutline = isDark ? new SKColor(15, 17, 22, 220) : new SKColor(60, 60, 60, 180);

        signal = new SKColor[HueTable.Length];
        category = new SKColor[HueTable.Length];

        for (var i = 0; i < HueTable.Length; i++)
        {
            signal[i] = isDark ? HueTable[i].SignalDark : HueTable[i].SignalLight;
            category[i] = isDark ? HueTable[i].CategoryDark : HueTable[i].CategoryLight;
        }
    }

    private readonly record struct HueColors(SKColor SignalDark, SKColor CategoryDark, SKColor SignalLight, SKColor CategoryLight);

    // One row per GraphHue, in enum order: socket/wire color and header band color for each theme.
    private static readonly HueColors[] HueTable =
    [
        /* Neutral */ new(new(158, 162, 171), new(100, 104, 113), new(95, 100, 110), new(192, 196, 204)),
        /* Slate   */ new(new(115, 149, 172), new(74, 98, 115), new(52, 102, 134), new(156, 186, 207)),
        /* Maroon  */ new(new(212, 108, 131), new(150, 58, 79), new(192, 32, 72), new(240, 148, 168)),
        /* Red     */ new(new(233, 80, 80), new(163, 40, 40), new(220, 24, 24), new(248, 142, 142)),
        /* Orange  */ new(new(235, 115, 45), new(152, 76, 30), new(222, 88, 0), new(250, 168, 116)),
        /* Amber   */ new(new(231, 186, 74), new(158, 124, 38), new(196, 134, 0), new(246, 206, 118)),
        /* Olive   */ new(new(202, 202, 60), new(126, 126, 46), new(146, 146, 0), new(224, 224, 120)),
        /* Green   */ new(new(108, 222, 138), new(52, 158, 79), new(0, 164, 58), new(132, 230, 160)),
        /* Emerald */ new(new(38, 209, 157), new(36, 126, 97), new(0, 166, 112), new(118, 232, 196)),
        /* Teal    */ new(new(70, 199, 199), new(50, 126, 126), new(0, 154, 154), new(128, 224, 224)),
        /* Cyan    */ new(new(76, 188, 235), new(35, 126, 163), new(0, 134, 194), new(126, 206, 242)),
        /* Blue    */ new(new(99, 161, 255), new(24, 90, 192), new(18, 100, 235), new(140, 188, 252)),
        /* Indigo  */ new(new(135, 135, 238), new(44, 44, 172), new(78, 78, 224), new(168, 168, 246)),
        /* Purple  */ new(new(172, 115, 230), new(108, 50, 168), new(132, 48, 220), new(200, 158, 246)),
        /* Magenta */ new(new(212, 86, 188), new(144, 52, 126), new(190, 20, 152), new(240, 146, 216)),
        /* Pink    */ new(new(233, 146, 196), new(166, 52, 118), new(222, 52, 136), new(248, 162, 206)),
    ];

    private static SKColor ToSK(System.Drawing.Color c) => new(c.R, c.G, c.B, c.A);
}
