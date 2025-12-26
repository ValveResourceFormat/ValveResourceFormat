using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GUI.Utils;
using Svg.Skia;

namespace GUI.Controls;

public class ThemedToolStripMenuItem : ToolStripMenuItem
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override Image? Image
    {
        get => base.Image;
        set => base.Image = value;
    }

#if DEBUG
    [TypeConverter(typeof(SvgResourceNameConverter))]
#endif
    [Description("Will override the image from the designer"), Category("Appearance")]
    public string SVGImageResourceName
    {
        get => field;
        set
        {
            field = value;

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            LoadSvgImage();
        }
    } = string.Empty;

    protected override void OnOwnerChanged(EventArgs e)
    {
        base.OnOwnerChanged(e);

        LoadSvgImage();
    }

    private void LoadSvgImage()
    {
        if (string.IsNullOrEmpty(SVGImageResourceName))
        {
            return;
        }

        if (Owner is null)
        {
            return;
        }

        var resourceName = SVGImageResourceName;
        Stream? svgResource = null;

        // Try loading light variant if in light mode
        if (Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic)
        {
            var lightVariantName = $"{resourceName.AsSpan()[..^4]}_light.svg";
            svgResource = Program.Assembly.GetManifestResourceStream(lightVariantName);
        }

        svgResource
            ??= Program.Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Failed to find resource `{resourceName}` for SVG icon in ${nameof(ThemedToolStripMenuItem)}.");

        using (svgResource)
        {
            using var svg = new SKSvg();
            svg.Load(svgResource);
            Image = Themer.SvgToBitmap(svg, Owner.ImageScalingSize.Width, Owner.ImageScalingSize.Height);
        }
    }
}
