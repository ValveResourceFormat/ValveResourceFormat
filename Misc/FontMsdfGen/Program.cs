// Run this with `dotnet run`. Place the exe and ttf in the same working directory.

using System.Diagnostics;

var msdfgenPath = "./msdf-atlas-gen.exe";
var fontFilePath = "./JetBrainsMono-Regular.ttf";
var pxRange = 16;

if (!File.Exists(fontFilePath))
{
    Console.Error.WriteLine($"{fontFilePath} does not exist. Download it from https://www.jetbrains.com/lp/mono/");
    return 1;
}

if (!File.Exists(msdfgenPath))
{
    Console.Error.WriteLine($"{fontFilePath} does not exist. Download it from https://github.com/Chlumsky/msdf-atlas-gen/releases");
    return 1;
}

var metrics = new FontMetric[128];

using Process process = new()
{
    StartInfo =
    {
        FileName = msdfgenPath,
        Arguments = $"-font {fontFilePath} -imageout output.png -json output.json -pxrange {pxRange} -charset charset.txt -yorigin top -dimensions 512 512 -scanline",
        UseShellExecute = false,
    }
};
process.Start();
process.WaitForExit();

var json = System.Text.Json.JsonSerializer.Deserialize<MetricsFile>(File.ReadAllText("./output.json"));

if (json == null || json.atlas.width != json.atlas.height)
{
    throw new InvalidDataException();
}

Console.WriteLine();
Console.WriteLine($"// Font metrics for {Path.GetFileName(fontFilePath)} generated using msdf-atlas-gen (use Misc/FontMsdfGen)");
Console.WriteLine($"private const float AtlasSize = {json.atlas.width}f;");
Console.WriteLine($"private const float Ascender = {json.metrics.ascender}f;");
Console.WriteLine($"private const float Descender = {json.metrics.descender}f;");
Console.WriteLine($"private const float LineHeight = {json.metrics.lineHeight}f;");

var next = 32;

foreach (var t in json.glyphs)
{
    if (next++ != t.unicode)
    {
        throw new InvalidDataException();
    }

    if (t.unicode == ' ')
    {
        Console.WriteLine($"private const float DefaultAdvance = {t.advance}f;");
        continue;
    }

    ArgumentNullException.ThrowIfNull(t.planeBounds);
    ArgumentNullException.ThrowIfNull(t.atlasBounds);

    metrics[t.unicode] = new(
        new(t.planeBounds.left, t.planeBounds.top, t.planeBounds.right, t.planeBounds.bottom),
        new(t.atlasBounds.left, t.atlasBounds.top, t.atlasBounds.right, t.atlasBounds.bottom),
        t.advance
    );
}

Console.WriteLine($"private const float TextureRange = {json.atlas.distanceRange / (float)json.atlas.width}f;");
Console.WriteLine("private static readonly FontMetric[] FontMetrics =");
Console.WriteLine("[");

foreach (var metric in metrics)
{
    if (metric == null)
    {
        continue;
    }

    var line = $"\tnew(new({metric.PlaneBounds.X}f, {metric.PlaneBounds.Y}f, {metric.PlaneBounds.Z}f, {metric.PlaneBounds.W}f), new({metric.AtlasBounds.X}f, {metric.AtlasBounds.Y}f, {metric.AtlasBounds.Z}f, {metric.AtlasBounds.W}f), {metric.Advance}f),";

    Console.WriteLine(line);
}

Console.WriteLine("];");

return 0;

record FontMetric(Vector4 PlaneBounds, Vector4 AtlasBounds, float Advance);

#pragma warning disable CA1812, IDE1006
class Bounds
{
    public float left { get; set; }
    public float top { get; set; }
    public float right { get; set; }
    public float bottom { get; set; }
}

class Glyph
{
    public int unicode { get; set; }
    public float advance { get; set; }
    public Bounds? planeBounds { get; set; }
    public Bounds? atlasBounds { get; set; }
}

class Atlas
{
    public int distanceRange { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

class Metrics
{
    public float emSize { get; set; }
    public float lineHeight { get; set; }
    public float ascender { get; set; }
    public float descender { get; set; }
    public float underlineY { get; set; }
    public float underlineThickness { get; set; }
}

class MetricsFile
{
    public required Atlas atlas { get; set; }
    public required Metrics metrics { get; set; }
    public required Glyph[] glyphs { get; set; }
}
