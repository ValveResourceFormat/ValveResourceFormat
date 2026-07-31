using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Svg.Skia;
using ValveResourceFormat.IO;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace GUI.Utils;

// Global icon cache for the app, loaded from embedded resources at startup.
static class AppIcons
{
    // This list intentionally lives for the entire process lifetime and is never disposed,
    // disposing it makes closing the GUI very slow.
    //
    // Never lookup icons from this list, use Icons and ExtensionIcons properties.
    public static ImageList ImageList { get; } = new ImageList
    {
        ColorDepth = ColorDepth.Depth32Bit,
    };

    /// <summary>
    /// Lookup a UI icon from GUI/Icons/ folder.
    /// </summary>
    public static Dictionary<string, int> Icons { get; } = [];

    /// <summary>
    /// Lookup a file extension icon from GUI/Icons/AssetTypes/ folder.
    /// </summary>
    public static Dictionary<string, int> ExtensionIcons { get; } = [];

    /// <summary>
    /// Lookup any loaded icon as SVG by name. Contains both UI icons (GUI/Icons/) and
    /// file extension icons (GUI/Icons/AssetTypes/).
    /// </summary>
    public static Dictionary<string, SKSvg> ExtensionSVGS { get; } = [];

    /// <summary>
    /// Lookup a game icon by appid that is loaded by the Explorer control from Steam.
    /// </summary>
    public static ConcurrentDictionary<int, int> GameIcons { get; } = new();

    public static void Load(int iconSize)
    {
        ImageList.ImageSize = new Size(iconSize, iconSize);

        var resources = Program.Assembly.GetManifestResourceNames().Where(static r => r.StartsWith("GUI.Icons.", StringComparison.Ordinal));

        if (Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic)
        {
            // In light mode, sort icons so that _light icons come first
            resources = resources.OrderByDescending(static r => r.Contains("_light", StringComparison.Ordinal));
        }
        else
        {
            // In dark mode, just filter out all _light icons
            resources = resources.Where(static r => !r.Contains("_light", StringComparison.Ordinal));
        }

        const string AssetTypesAliasesFile = "GUI.Icons.AssetTypes.aliases.txt";

        foreach (var fullName in resources)
        {
            if (fullName == AssetTypesAliasesFile)
            {
                continue;
            }

            var name = fullName.AsSpan("GUI.Icons.".Length);
            var extension = Path.GetExtension(name);
            name = Path.GetFileNameWithoutExtension(name);

            var isAssetType = name.StartsWith("AssetTypes.", StringComparison.Ordinal);
            var isLightIcon = name.EndsWith("_light", StringComparison.Ordinal);

            if (isAssetType)
            {
                name = name["AssetTypes.".Length..];
            }

            if (isLightIcon)
            {
                name = name[..^"_light".Length];
            }

            using var stream = Program.Assembly.GetManifestResourceStream(fullName);
            Debug.Assert(stream is not null);

            var iconName = name.ToString();
            var index = ImageList.Images.Count;

            if (isAssetType)
            {
                if (!ExtensionIcons.TryAdd(iconName, index))
                {
                    continue;
                }
            }
            else if (!Icons.TryAdd(iconName, index))
            {
                continue;
            }

            if (extension.SequenceEqual(".svg"))
            {
#pragma warning disable CA2000 // Dispose objects before losing scope, this is a false positive
                var svg = new SKSvg();
                svg.Load(stream);

                ExtensionSVGS.TryAdd(iconName, svg);

                using var bitmap = Themer.SvgToBitmap(svg, ImageList.ImageSize.Width, ImageList.ImageSize.Height);
                AddFixedImageToImageList(bitmap, ImageList);
#pragma warning restore CA2000
            }
            else
            {
                Debug.Assert(false, "Use only svg icons");
            }
        }

        {
            using var stream = Program.Assembly.GetManifestResourceStream(AssetTypesAliasesFile);
            Debug.Assert(stream != null);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var space = line.IndexOf(' ', StringComparison.Ordinal);
                var addResult = ExtensionIcons.TryAdd(line[..space], ExtensionIcons[line[(space + 1)..]]);
                var addResultSVG = ExtensionSVGS.TryAdd(line[..space], ExtensionSVGS[line[(space + 1)..]]);
                Debug.Assert(addResult, "Duplicate icon");
                Debug.Assert(addResultSVG, "Duplicate SVG icon");
            }
        }
    }

    public static int GetImageIndexForExtension(ReadOnlySpan<char> extension)
    {
        if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            extension = extension[0..^2];
        }

        var lookup = ExtensionIcons.GetAlternateLookup<ReadOnlySpan<char>>();

        if (lookup.TryGetValue(extension, out var image))
        {
            return image;
        }

        if (extension.Length > 0 && extension[0] == 'v' && lookup.TryGetValue(extension[1..], out image))
        {
            return image;
        }

        return Icons["File"];
    }

    // Based on https://www.codeproject.com/articles/Adding-and-using-32-bit-alphablended-images-and-ic
    // Fixes adding images with proper transparency without incorrect anti aliasing
    public static unsafe void AddFixedImageToImageList(Bitmap bm, ImageList il)
    {
        Debug.Assert(bm.Size == il.ImageSize);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biWidth = bm.Width;
        bmi.bmiHeader.biHeight = bm.Height;

        bm.RotateFlip(RotateFlipType.RotateNoneFlipY);

        using var hBitmap = PInvoke.CreateDIBSection((HDC)IntPtr.Zero, &bmi, DIB_USAGE.DIB_RGB_COLORS, out var ppvBits, null, 0);

        var bitmapData = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var byteCount = bm.Height * bitmapData.Stride;
        Buffer.MemoryCopy((void*)bitmapData.Scan0, ppvBits, byteCount, byteCount);
        bm.UnlockBits(bitmapData);

        using var ilHandle = new DeleteObjectSafeHandle(il.Handle, ownsHandle: false);
        PInvoke.ImageList_Add(ilHandle, hBitmap, default);
    }
}
