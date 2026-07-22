using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Headless driver that lays out a graph once per improvement, renders each result to a PNG and
/// scores it, so a layout change can be compared side by side instead of by memory. Runs without
/// a window or a GL context: <see cref="GraphView.RenderToCanvas"/> only needs a raster canvas.
/// </summary>
internal static class GraphLayoutLab
{
    /// <summary>One rendered layout: what was enabled, what it measured, where the image went.</summary>
    private sealed record Shot(string Stage, string Description, GraphLayoutMetrics Metrics, string CurvedFile, string StraightFile);

    /// <summary>One layout worth comparing, with how to apply it to a freshly built view.</summary>
    private sealed record Variant(string Name, string Description, Action<GraphView> Apply);

    /// <summary>
    /// The layouts under comparison. The two library ones are what upstream master ships and are
    /// run through this same renderer so the numbers mean the same thing.
    /// </summary>
    private static readonly Variant[] Variants =
    [
        new("2-branch-shipped", "This branch as it stands on the branch tip, no improvements enabled",
            static view =>
            {
                view.LayoutOptions = Tune(new GraphLayoutOptions { Features = GraphLayoutFeature.None, LayerSpacing = 220f, NodeSpacing = 36f });
                view.LayoutNodesPacked();
            }),

        new("3-branch-swaponly", "Shipped layout plus only the crossing repair: swap, reinsert and slide cards",
            static view =>
            {
                view.LayoutOptions = Tune(new GraphLayoutOptions
                {
                    Features = GraphLayoutFeature.CrossingSwap,
                    LayerSpacing = 220f,
                    NodeSpacing = 36f,
                });
                view.LayoutNodesPacked();
            }),

        new("4-branch-tight", "Every general-purpose improvement, at this branch's original tighter spacing",
            static view =>
            {
                view.LayoutOptions = Tune(new GraphLayoutOptions { LayerSpacing = 220f, NodeSpacing = 36f });
                view.LayoutNodesPacked();
            }),

        new("5-branch-wide", "Every general-purpose improvement, at the settled spacing (320 / 44)",
            static view =>
            {
                view.LayoutOptions = Tune(new GraphLayoutOptions());
                view.LayoutNodesPacked();
            }),

        new("6-branch-final", "The kept set: alignment, barycentre, crossing repair, and dummies only on animation graphs",
            static view =>
            {
                var features = GraphLayoutFeature.PortAwareAlignment
                    | GraphLayoutFeature.BarycentreRepair
                    | GraphLayoutFeature.CrossingSwap;

                // Dummies are the one feature whose value flips with graph shape: essential on a
                // single connected animation DAG, actively harmful on many-island entity graphs.
                if (animationGraph)
                {
                    features |= GraphLayoutFeature.LongWireDummies;
                }

                view.LayoutOptions = Tune(new GraphLayoutOptions { Features = features });
                view.LayoutNodesPacked();
            }),
    ];

    /// <summary>Whether the case being measured is an animation graph rather than entity or pulse.</summary>
    private static bool animationGraph;

    /// <summary>Restricts the --node diagnostic to cards whose title contains this.</summary>
    private static string? nodeFilter;

    private static int budgetOverride = -1;

    private static GraphLayoutOptions Tune(GraphLayoutOptions options)
    {
        if (budgetOverride >= 0)
        {
            options.CrossingRepairBudgetMs = budgetOverride;
        }

        return options;
    }

    /// <summary>The improvements, for the leave-one-out study.</summary>
    private static readonly (GraphLayoutFeature Feature, string Name)[] Features =
    [
        (GraphLayoutFeature.PortAwareAlignment, "port-align"),
        (GraphLayoutFeature.BarycentreRepair, "barycentre"),
        (GraphLayoutFeature.LongWireDummies, "dummies"),
        (GraphLayoutFeature.CrossingSwap, "swap-nodes"),
    ];

    /// <summary>
    /// Scores the full feature set against the same set with one feature removed, per graph. A
    /// feature whose removal costs nothing is not paying for the code that implements it.
    /// </summary>
    private static void Ablate(List<(string Name, Func<(GraphView View, IDisposable? Owner)> Build)> cases, string outputDir)
    {
        var rows = new List<(string Config, Dictionary<string, int> Crossings, double Millis)>();

        var configs = new List<(string Name, GraphLayoutFeature Features)>
        {
            ("none", GraphLayoutFeature.None),
            ("all", GraphLayoutFeature.All),
        };

        foreach (var (feature, name) in Features)
        {
            configs.Add(($"all minus {name}", GraphLayoutFeature.All & ~feature));
            configs.Add(($"only {name}", feature));
        }

        foreach (var (configName, features) in configs)
        {
            var crossings = new Dictionary<string, int>();
            var millis = 0d;

            foreach (var (caseName, build) in cases)
            {
                GraphLayoutOptions.Default = new GraphLayoutOptions { Features = GraphLayoutFeature.None };

                GraphView view;
                IDisposable? owner;

                try
                {
                    (view, owner) = build();
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    view.LayoutOptions = Tune(new GraphLayoutOptions { Features = features });
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    view.LayoutNodesPacked();
                    clock.Stop();
                    millis += clock.Elapsed.TotalMilliseconds;

                    crossings[caseName] = GraphLayoutScorer.Measure(view.Nodes, view.Wires, view.Geometry).WireCrossings;
                }
                finally
                {
                    owner?.Dispose();

                    if (owner == null)
                    {
                        view.Dispose();
                    }
                }
            }

            rows.Add((configName, crossings, millis));
            Console.WriteLine($"  {configName,-24} {string.Join("  ", crossings.Select(c => $"{Shorten(c.Key)}={c.Value,6}"))}  {millis,7:F0}ms");
        }

        var csv = new StringBuilder();
        csv.Append("config");

        foreach (var (caseName, _) in cases)
        {
            csv.Append(CultureInfo.InvariantCulture, $",{caseName}");
        }

        csv.AppendLine(",total_ms");

        foreach (var (configName, crossings, millis) in rows)
        {
            csv.Append(configName);

            foreach (var (caseName, _) in cases)
            {
                csv.Append(CultureInfo.InvariantCulture, $",{crossings.GetValueOrDefault(caseName, -1)}");
            }

            csv.AppendLine(CultureInfo.InvariantCulture, $",{millis:F0}");
        }

        File.WriteAllText(Path.Combine(outputDir, "ablation.csv"), csv.ToString());
        Console.WriteLine($"Wrote {Path.Combine(outputDir, "ablation.csv")}");
    }

    private static string Shorten(string name)
    {
        var trimmed = name.Split('-')[0];
        return trimmed.Length > 8 ? trimmed[..8] : trimmed;
    }

    // Delegates the library layout needs; it is deliberately ignorant of this graph model.

    /// <summary>Largest edge of a rendered image, in pixels; raise it with --maxedge.</summary>
    private static int maxImageEdge = 2600;

    /// <summary>Substrings an asset path must contain to be rendered; empty renders everything.</summary>
    private static string[] pathFilters = [];

    /// <summary>
    /// Packages and loader contexts shared by every stage of a run. Building one context per
    /// stage would re-preload every game vpk each time, so they are opened once and released
    /// together when the run finishes.
    /// </summary>
    private static readonly List<IDisposable> Shared = [];

    private static readonly Dictionary<string, (VrfGuiContext Gui, RendererContext Renderer)> Contexts = [];

    private static (VrfGuiContext Gui, RendererContext Renderer) ContextFor(string path, SteamDatabase.ValvePak.Package? package)
    {
        if (Contexts.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var gui = new VrfGuiContext(path, null) { CurrentPackage = package };
        var renderer = new RendererContext(gui, VrfGuiContext.Logger);

        Shared.Add(renderer);
        Shared.Add(gui);
        Contexts[path] = (gui, renderer);
        return (gui, renderer);
    }

    private static void ReleaseShared()
    {
        Contexts.Clear();

        for (var i = Shared.Count - 1; i >= 0; i--)
        {
            Shared[i].Dispose();
        }

        Shared.Clear();
    }

    public static int Run(string[] args)
    {
        var inputs = args.Where(static a => !a.StartsWith('-')).ToList();
        var outputDir = ArgValue(args, "--out") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "graph-layout-lab");

        Directory.CreateDirectory(outputDir);

        var placements = ArgValue(args, "--placement") is "organic"
            ? new[] { GraphPlacement.Organic }
            : ArgValue(args, "--placement") is "both"
                ? [GraphPlacement.Layered, GraphPlacement.Organic]
                : [GraphPlacement.Layered];

        var cases = new List<(string Name, Func<(GraphView View, IDisposable? Owner)> Build)>();

        var limit = int.TryParse(ArgValue(args, "--limit"), out var parsed) ? parsed : int.MaxValue;

        if (int.TryParse(ArgValue(args, "--maxedge"), out var edge))
        {
            maxImageEdge = edge;
        }

        pathFilters = ArgValue(args, "--match")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var explain = args.Contains("--explain");
        nodeFilter = ArgValue(args, "--node");

        if (int.TryParse(ArgValue(args, "--budget"), out var budget))
        {
            budgetOverride = budget;
        }

        foreach (var input in inputs)
        {
            cases.AddRange(CollectCases(input).Take(limit));
        }

        if (cases.Count == 0)
        {
            Console.WriteLine("No assets given, using the built-in synthetic graphs.");

            foreach (var (name, build) in GraphLayoutLabFixtures.All)
            {
                cases.Add((name, () => (build(), null)));
            }
        }

        if (args.Contains("--ablate"))
        {
            Console.WriteLine("=== leave-one-out feature study (crossings per graph) ===");
            Ablate(cases, outputDir);
            ReleaseShared();
            return 0;
        }

        var report = new StringBuilder();
        var csv = new StringBuilder();
        csv.AppendLine(GraphLayoutMetrics.CsvHeader);

        var sections = new List<(string Title, List<Shot> Shots)>();

        foreach (var (name, build) in cases)
        {
            foreach (var placement in placements)
            {
                var title = placements.Length > 1 ? $"{name} ({placement})" : name;
                animationGraph = name.Contains("nmgraph", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("vagrp", StringComparison.OrdinalIgnoreCase);

                Console.WriteLine($"=== {title} ===");

                var shots = new List<Shot>();

                foreach (var variant in Variants)
                {
                    GraphView view;
                    IDisposable? owner;

                    // Every variant is built with the improvements off, so the constructor's own
                    // layout costs the same for all of them and the build column stays comparable.
                    // It also keeps port ordering out of the build, since it permanently permutes
                    // socket rows and would otherwise leak from one variant into the next.
                    GraphLayoutOptions.Default = new GraphLayoutOptions { Features = GraphLayoutFeature.None };

                    var buildClock = System.Diagnostics.Stopwatch.StartNew();

                    try
                    {
                        (view, owner) = build();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"  {variant.Name}: build failed: {e.Message}");
                        break;
                    }

                    buildClock.Stop();

                    try
                    {
                        view.Placement = placement;

                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        variant.Apply(view);
                        clock.Stop();

                        var slug = $"{Sanitize(title)}-{variant.Name}";
                        var curved = Render(view, Path.Combine(outputDir, $"{slug}.png"), straight: false, out var canvasMs);
                        var straight = Render(view, Path.Combine(outputDir, $"{slug}-straight.png"), straight: true);

                        var metrics = GraphLayoutScorer.Measure(view.Nodes, view.Wires, view.Geometry) with
                        {
                            BuildMilliseconds = buildClock.Elapsed.TotalMilliseconds,
                            LayoutMilliseconds = clock.Elapsed.TotalMilliseconds,
                            RenderMilliseconds = canvasMs,
                        };

                        shots.Add(new Shot(variant.Name, variant.Description, metrics, curved, straight));
                        csv.AppendLine(metrics.ToCsvRow($"{title}/{variant.Name}"));

                        var islands = view.GetComponents();

                        Console.WriteLine(
                            $"  {variant.Name,-18} islands {islands.Count,4} (largest {islands.Max(static i => i.Count),5})  " +
                            $"crossings {metrics.WireCrossings,6}  over-cards {metrics.WiresOverNodes,6}  " +
                            $"area {metrics.Area,7:F1}Mpx  build {metrics.BuildMilliseconds,6:F0}ms  " +
                            $"layout {metrics.LayoutMilliseconds,6:F0}ms  draw {metrics.RenderMilliseconds,7:F1}ms");

                        if (nodeFilter != null)
                        {
                            foreach (var line in GraphLayoutScorer.DescribeNodes(view.Nodes, view.Geometry, nodeFilter))
                            {
                                Console.WriteLine($"      {line}");
                            }
                        }

                        if (explain)
                        {
                            foreach (var crossing in GraphLayoutScorer.DescribeCrossings(view.Nodes, view.Wires, view.Geometry))
                            {
                                Console.WriteLine($"      {crossing}");
                            }
                        }
                    }
                    finally
                    {

                        if (owner != null)
                        {
                            owner.Dispose();
                        }
                        else
                        {
                            view.Dispose();
                        }
                    }
                }

                if (shots.Count > 0)
                {
                    sections.Add((title, shots));
                    AppendMarkdown(report, title, shots);
                }
            }
        }

        File.WriteAllText(Path.Combine(outputDir, "metrics.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(outputDir, "metrics.md"), report.ToString());

        var html = Path.Combine(outputDir, "index.html");
        File.WriteAllText(html, BuildHtml(sections, outputDir, embed: false));

        // Same page with every image inlined, so it survives being moved or sent on its own.
        var standalone = Path.Combine(outputDir, "index-standalone.html");
        File.WriteAllText(standalone, BuildHtml(sections, outputDir, embed: true));

        ReleaseShared();

        Console.WriteLine();
        Console.WriteLine($"Wrote {sections.Sum(static s => s.Shots.Count) * 2} images to {outputDir}");
        Console.WriteLine($"Open {html}");
        Console.WriteLine($"Portable single file: {standalone}");
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static bool Matches(string path)
        => pathFilters.Length == 0 || pathFilters.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string name)
        => string.Concat(name.Select(static c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));

    /// <summary>
    /// Expands one command line input into cases: a loose graph file, every graph file under a
    /// directory, or every graph resource packed inside a vpk.
    /// </summary>
    private static IEnumerable<(string Name, Func<(GraphView View, IDisposable? Owner)> Build)> CollectCases(string input)
    {
        if (Directory.Exists(input))
        {
            foreach (var file in Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories))
            {
                if (GraphExtensions.Contains(Path.GetExtension(file)))
                {
                    var captured = file;
                    yield return (Path.GetFileNameWithoutExtension(captured), () => BuildFromFile(captured));
                }
            }

            yield break;
        }

        if (!File.Exists(input))
        {
            yield break;
        }

        if (!input.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            yield return (Path.GetFileNameWithoutExtension(input), () => BuildFromFile(input));
            yield break;
        }

        var package = new SteamDatabase.ValvePak.Package();
        Shared.Add(package);
        package.Read(input);

        var mapName = Path.GetFileNameWithoutExtension(input);

        foreach (var extension in GraphExtensions)
        {
            if (package.Entries is null || !package.Entries.TryGetValue(extension.TrimStart('.'), out var entries))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var captured = entry;

                if (!Matches(captured.GetFullPath()))
                {
                    continue;
                }

                yield return ($"{mapName}-{Path.GetFileName(captured.GetFileName())}", () => BuildFromPackage(package, captured, input));
            }
        }
    }

    private static (GraphView View, IDisposable? Owner) BuildFromPackage(
        SteamDatabase.ValvePak.Package package,
        SteamDatabase.ValvePak.PackageEntry entry,
        string packagePath)
    {
        package.ReadEntry(entry, out var bytes);

        var resource = new Resource { FileName = entry.GetFullPath() };
        Shared.Add(resource);
        resource.Read(new MemoryStream(bytes));

        return BuildFromResource(resource, packagePath, package);
    }

    // AG2 ships as vanmgrph_c in HL Alyx and Deadlock but as vnmgraph_c in CS2; both map to
    // ResourceType.NmGraph, and leaving either out silently skips that game's animation graphs.
    private static readonly HashSet<string> GraphExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vents_c", ".vpulse_c", ".vanmgrph_c", ".vnmgraph_c", ".vagrp_c",
    };

    // Mirrors the dispatch in Types/Viewers/Resource.cs. The viewers are plain classes, not
    // controls, and everything touching GL is deferred to methods this never calls, so building
    // one headless just yields its graph.
    private static (GraphView View, IDisposable? Owner) BuildFromFile(string path)
    {
        var resource = new Resource { FileName = path };
        Shared.Add(resource);
        resource.Read(path);

        return BuildFromResource(resource, path, null);
    }

    private static (GraphView View, IDisposable? Owner) BuildFromResource(
        Resource resource,
        string contextPath,
        SteamDatabase.ValvePak.Package? package)
    {
        var (guiContext, rendererContext) = ContextFor(contextPath, package);

        GLGraphViewer viewer = resource.ResourceType switch
        {
            ResourceType.EntityLump => new EntityIOGraphViewer(guiContext, rendererContext, (EntityLump)resource.DataBlock!),
            ResourceType.PulseGraphDef => new PulseGraphViewer(guiContext, rendererContext, ((BinaryKV3)resource.DataBlock!).Data),
            ResourceType.NmGraph => new AnimationGraphViewer(guiContext, rendererContext, ((BinaryKV3)resource.DataBlock!).Data),
            ResourceType.AnimationGraph => new AG1GraphViewer(guiContext, rendererContext, ((AnimGraph)resource.DataBlock!).Data),
            _ => throw new NotSupportedException($"{resource.ResourceType} is not a graph resource."),
        };

        return (viewer.Graph, viewer);
    }

    private static string Render(GraphView view, string path, bool straight)
        => Render(view, path, straight, out _);

    /// <summary>
    /// Renders to a PNG. <paramref name="canvasMilliseconds"/> times only the draw itself, not the
    /// PNG encode or the file write, since only the draw is what the viewer does per frame.
    /// </summary>
    private static string Render(GraphView view, string path, bool straight, out double canvasMilliseconds)
    {
        view.StraightWires = straight;

        var bounds = view.GetGraphBounds();
        const float Margin = 60f;

        var width = bounds.Width + Margin * 2f;
        var height = bounds.Height + Margin * 2f;
        var scale = Math.Min(1f, maxImageEdge / Math.Max(width, height));

        var pixelWidth = Math.Max(1, (int)MathF.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)MathF.Ceiling(height * scale));

        using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(view.Palette.Canvas);
            canvas.Scale(scale, scale);
            canvas.Translate(-bounds.Left + Margin, -bounds.Top + Margin);

            var visible = new SKRect(
                bounds.Left - Margin, bounds.Top - Margin,
                bounds.Right + Margin, bounds.Bottom + Margin);

            view.RenderToCanvas(canvas, visible, scale);
        }

        clock.Stop();
        canvasMilliseconds = clock.Elapsed.TotalMilliseconds;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);

        return path;
    }

    private static void AppendMarkdown(StringBuilder report, string title, List<Shot> shots)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        report.AppendLine();
        report.AppendLine("| stage | crossings | over nodes | backward | straight | mean dock | total length |");
        report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var shot in shots)
        {
            var m = shot.Metrics;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {shot.Stage} | {m.WireCrossings} | {m.WiresOverNodes} | {m.BackwardWires} | {m.StraightWires} | {m.MeanDockOffset:F1} | {m.TotalWireLength:F0} |");
        }

        report.AppendLine();
    }

    private static string ImageSource(string file, string outputDir, bool embed)
        => embed
            ? "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(file))
            : Escape(Path.GetRelativePath(outputDir, file));

    private static string BuildHtml(List<(string Title, List<Shot> Shots)> sections, string outputDir, bool embed)
    {
        var html = new StringBuilder();

        html.AppendLine(HtmlHead);

        foreach (var (title, shots) in sections)
        {
            var baseline = shots[0].Metrics;

            html.AppendLine(CultureInfo.InvariantCulture, $"<section><h2>{Escape(title)}</h2>");
            html.AppendLine("<table><thead><tr><th>variant</th><th>what it is</th><th>crossings</th><th>over cards</th>"
                + "<th>area Mpx</th><th>length</th><th>build</th><th>layout</th><th>draw</th></tr></thead><tbody>");

            foreach (var shot in shots)
            {
                var m = shot.Metrics;
                html.AppendLine(CultureInfo.InvariantCulture,
                    $"<tr><td><a href=\"#{Escape(Sanitize(title))}-{shot.Stage}\">{shot.Stage}</a></td><td>{Escape(shot.Description)}</td>" +
                    $"<td class=\"lead\">{Delta(m.WireCrossings, baseline.WireCrossings)}</td>" +
                    $"<td>{Delta(m.WiresOverNodes, baseline.WiresOverNodes)}</td>" +
                    $"<td>{Delta(m.Area, baseline.Area)}</td>" +
                    $"<td>{Delta(m.TotalWireLength, baseline.TotalWireLength)}</td>" +
                    $"<td>{Millis(m.BuildMilliseconds)}</td>" +
                    $"<td>{Delta((float)m.LayoutMilliseconds, (float)baseline.LayoutMilliseconds)} ms</td>" +
                    $"<td>{Millis(m.RenderMilliseconds)}</td></tr>");
            }

            html.AppendLine("</tbody></table>");

            foreach (var shot in shots)
            {
                html.AppendLine(CultureInfo.InvariantCulture,
                    $"<figure id=\"{Escape(Sanitize(title))}-{shot.Stage}\"><figcaption><b>{shot.Stage}</b> {Escape(shot.Description)}</figcaption>");
                html.AppendLine("<div class=\"pair\">");
                html.AppendLine(CultureInfo.InvariantCulture, $"<div><span class=\"tag\">curved</span><img loading=\"lazy\" src=\"{ImageSource(shot.CurvedFile, outputDir, embed)}\"></div>");
                html.AppendLine(CultureInfo.InvariantCulture, $"<div><span class=\"tag\">straight</span><img loading=\"lazy\" src=\"{ImageSource(shot.StraightFile, outputDir, embed)}\"></div>");
                html.AppendLine("</div></figure>");
            }

            html.AppendLine("</section>");
        }

        html.AppendLine("</body>");
        return html.ToString();
    }

    private static string Millis(double value)
        => value >= 10d
            ? value.ToString("F0", CultureInfo.InvariantCulture) + " ms"
            : value.ToString("F1", CultureInfo.InvariantCulture) + " ms";

    private static string Delta(float value, float baseline, bool higherIsBetter = false)
    {
        var text = Math.Abs(value - MathF.Round(value)) < 0.05f
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);

        if (Math.Abs(value - baseline) < 0.05f)
        {
            return text;
        }

        var better = higherIsBetter ? value > baseline : value < baseline;
        var change = baseline == 0f ? 0f : (value - baseline) / baseline * 100f;
        var sign = value > baseline ? "+" : string.Empty;

        return $"{text} <span class=\"{(better ? "good" : "bad")}\">{sign}{change.ToString("F0", CultureInfo.InvariantCulture)}%</span>";
    }

    private static string Escape(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string HtmlHead = """
        <!doctype html>
        <meta charset="utf-8">
        <title>Graph layout lab</title>
        <style>
        :root { color-scheme: dark; }
        body { margin: 0; padding: 24px 32px 64px; background: #14161a; color: #d6dae0;
               font: 14px/1.55 "Segoe UI", system-ui, sans-serif; }
        h1 { font-size: 22px; margin: 0 0 4px; }
        h2 { font-size: 18px; margin: 40px 0 12px; padding-bottom: 6px; border-bottom: 1px solid #2a2f37; }
        p.lede { color: #8a929e; margin: 0 0 24px; max-width: 70ch; }
        table { border-collapse: collapse; margin: 0 0 24px; font-variant-numeric: tabular-nums; }
        th, td { padding: 5px 12px; text-align: right; border-bottom: 1px solid #23272e; }
        th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
        thead th { color: #8a929e; font-weight: 600; }
        td a { color: #6cb6ff; text-decoration: none; }
        td.lead { font-weight: 600; }
        .good { color: #57c98a; font-size: 12px; }
        .bad { color: #e5735f; font-size: 12px; }
        figure { margin: 0 0 32px; }
        figcaption { color: #8a929e; margin-bottom: 8px; }
        figcaption b { color: #d6dae0; }
        .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
        .pair > div { position: relative; min-width: 0; }
        .tag { position: absolute; top: 8px; left: 8px; background: #0009; padding: 2px 8px;
               border-radius: 4px; font-size: 11px; letter-spacing: .04em; text-transform: uppercase; }
        img { width: 100%; height: auto; display: block; border: 1px solid #2a2f37; border-radius: 6px; background: #1b1e24; }
        @media (max-width: 1100px) { .pair { grid-template-columns: 1fr; } }
        </style>
        <body>
        <h1>Graph layout lab</h1>
        <p class="lede">Five layouts of the same graph in the same renderer. Two are what upstream master
        ships, three are this branch. Percentages compare against upstream's layered layout; green is better.
        Crossings is the column that matters most, then area (compactness). Left image is the curved wire
        style, right is straight.</p>
        """;
}
