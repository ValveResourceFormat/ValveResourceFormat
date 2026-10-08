using System.Globalization;
using System.Text;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

// Counts every particle function class used across a VPK's particle systems, by list, and whether
// it is implemented, writing a markdown inventory sorted by how many files each class appears in.
//
// Usage: ParticleCapture audit <pak01_dir.vpk> <output.md>
internal static class ParticleAudit
{
    private static readonly (string Title, string Key, Func<string, bool> IsSupported)[] Lists =
    [
        ("Pre-emission operators", "m_PreEmissionOperators", ParticleSupportInfo.IsPreEmissionOperatorSupported),
        ("Emitters", "m_Emitters", ParticleSupportInfo.IsEmitterSupported),
        ("Initializers", "m_Initializers", ParticleSupportInfo.IsInitializerSupported),
        ("Operators", "m_Operators", ParticleSupportInfo.IsOperatorSupported),
        ("Force generators", "m_ForceGenerators", ParticleSupportInfo.IsForceGeneratorSupported),
        ("Constraints", "m_Constraints", ParticleSupportInfo.IsConstraintSupported),
        ("Renderers", "m_Renderers", ParticleRendererFactory.IsSupported),
    ];

    public static int Run(string vpkPath, string outputPath)
    {
        using var package = new Package();
        package.Read(vpkPath);
        using var fileLoader = new GameFileLoader(package, vpkPath);

        if (package.Entries == null || !package.Entries.TryGetValue("vpcf_c", out var entries))
        {
            Console.Error.WriteLine("No particle systems in this package");
            return 1;
        }

        // List key -> class -> files using it
        var usage = Lists.ToDictionary(list => list.Key, _ => new Dictionary<string, int>());
        var examples = new Dictionary<string, string>();
        var fullySupported = 0;
        var failed = 0;

        foreach (var entry in entries)
        {
            var path = entry.GetFullPath();
            KVObject data;

            try
            {
                using var resource = fileLoader.LoadFileCompiled(path[..^2]);

                if (resource?.DataBlock is not ParticleSystem system)
                {
                    failed++;
                    continue;
                }

                data = system.GetUpgradedData();
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or UnexpectedMagicException)
            {
                failed++;
                continue;
            }

            var everythingSupported = true;

            foreach (var (_, key, isSupported) in Lists)
            {
                var seen = new HashSet<string>();

                foreach (var function in data.GetArray(key) ?? [])
                {
                    if (function.GetBooleanProperty("m_bDisableOperator"))
                    {
                        continue;
                    }

                    var className = function.GetStringProperty("_class");
                    everythingSupported &= isSupported(className);

                    if (seen.Add(className))
                    {
                        usage[key][className] = usage[key].GetValueOrDefault(className) + 1;
                        examples.TryAdd(className, path);
                    }
                }
            }

            if (everythingSupported)
            {
                fullySupported++;
            }
        }

        var loaded = entries.Count - failed;
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Scanned {entries.Count} particle systems ({failed} failed to load).");
        report.AppendLine(CultureInfo.InvariantCulture, $"{fullySupported} of {loaded} ({100.0 * fullySupported / Math.Max(1, loaded):0.0}%) use only implemented classes.");
        report.AppendLine();

        foreach (var (title, key, isSupported) in Lists)
        {
            var classes = usage[key].OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToList();
            var implemented = classes.Count(pair => isSupported(pair.Key));

            report.AppendLine(CultureInfo.InvariantCulture, $"## {title} ({implemented} of {classes.Count} implemented)");
            report.AppendLine();
            report.AppendLine("| Class | Files | Implemented | Example |");
            report.AppendLine("| --- | ---: | :---: | --- |");

            foreach (var (className, files) in classes)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"| `{className}` | {files} | {(isSupported(className) ? "yes" : "**no**")} | `{examples[className]}` |");
            }

            report.AppendLine();
        }

        File.WriteAllText(outputPath, report.ToString());
        Console.WriteLine(report.ToString().Split('\n')[1]);
        return 0;
    }
}
