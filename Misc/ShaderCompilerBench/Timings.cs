using System.Diagnostics;
using System.Globalization;

namespace ShaderCompilerBench;

/// <summary>
/// Collects one millisecond sample per stage per iteration and prints the distribution at the end.
/// Stages keep the order they were first measured in, which is the order they run in.
/// </summary>
internal sealed class Timings(string title)
{
    private readonly List<string> order = [];
    private readonly Dictionary<string, List<double>> samples = [];

    public string Title { get; } = title;

    /// <summary>Set when a path could not run at all, in which case the report explains why instead of printing numbers.</summary>
    public string? Skipped { get; set; }

    /// <summary>Free-form lines printed under the table, for sizes and compiler versions.</summary>
    public List<string> Notes { get; } = [];

    public void Add(string stage, double milliseconds)
    {
        if (!samples.TryGetValue(stage, out var list))
        {
            list = [];
            samples[stage] = list;
            order.Add(stage);
        }

        list.Add(milliseconds);
    }

    public T Measure<T>(string stage, Func<T> action)
    {
        var start = Stopwatch.GetTimestamp();
        var result = action();
        Add(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return result;
    }

    public void Measure(string stage, Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        Add(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>
    /// Rows whose name starts with this are the compiler's own accounting of work already counted
    /// by the stages around them, so they are printed but left out of the total.
    /// </summary>
    public const string InformationalPrefix = "~";

    /// <summary>
    /// A stage belongs to an offline compiler when it is named after one. Everything else is work
    /// the driver does, which is the half that no choice of shading language can remove.
    /// </summary>
    private static bool IsCompilerStage(string stage)
        => stage.StartsWith("slang:", StringComparison.Ordinal)
        || stage.StartsWith("glslang:", StringComparison.Ordinal)
        || stage.StartsWith("spirv-opt:", StringComparison.Ordinal);

    private double MedianSum(Func<string, bool> predicate) => order
        .Where(stage => !stage.StartsWith(InformationalPrefix, StringComparison.Ordinal) && predicate(stage))
        .Sum(stage => Median(samples[stage]));

    /// <summary>Sum of the medians of every measured stage, which is the number the paths are compared on.</summary>
    public double MedianTotal => MedianSum(static _ => true);

    /// <summary>The part spent in an offline compiler, which is what adopting one costs.</summary>
    public double MedianCompilerTotal => MedianSum(IsCompilerStage);

    /// <summary>The part spent inside the GL driver, which every path pays in some form.</summary>
    public double MedianDriverTotal => MedianSum(static stage => !IsCompilerStage(stage));

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    public void Report()
    {
        Console.WriteLine();
        Console.WriteLine(Title);
        Console.WriteLine(new string('=', Title.Length));

        foreach (var note in Notes)
        {
            Console.WriteLine($"  {note}");
        }

        if (Skipped != null)
        {
            Console.WriteLine($"  SKIPPED: {Skipped}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  {"stage",-38} {"median",8} {"min",8} {"max",8} {"n",4}");
        Console.WriteLine($"  {new string('-', 38)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} ----");

        foreach (var stage in order)
        {
            var values = samples[stage];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {stage,-38} {Median(values),8:F3} {values.Min(),8:F3} {values.Max(),8:F3} {values.Count,4}"));
        }

        Console.WriteLine($"  {new string('-', 38)} {new string('-', 8)}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {"offline compiler (sum of medians)",-38} {MedianCompilerTotal,8:F3} ms"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {"GL driver (sum of medians)",-38} {MedianDriverTotal,8:F3} ms"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {"total",-38} {MedianTotal,8:F3} ms"));
    }
}
