using System.Diagnostics;

/// <summary>
/// The animation graph definitions in Renderer/AnimLib.
/// </summary>
sealed class AnimLibTarget : SchemaTarget
{
    private static readonly Dictionary<string, string> FolderByRootClass = new()
    {
        { "GraphNode", "Nodes" },
        { "Event", "Events" },
        { "PoseTask", "Tasks" },
    };

    public override string Name => "animlib";

    public override string DestinationDir => Path.Combine("Renderer", "AnimLib");

    public override string DestinationNameSpace => "ValveResourceFormat.Renderer.AnimLib";

    public override string BuildProject => "Renderer";

    public override string SourceNameSpace => "Nm";

    // Only the graph system's own classes: editor document classes are not part of the compiled resources,
    // and client module nodes are defined alongside the client code that registers them
    public override bool IsIncluded(string module, string name)
        => module == "animlib" && !name.Contains("Doc", StringComparison.Ordinal);

    public override IReadOnlyDictionary<string, string> TypeMap { get; } = new Dictionary<string, string>
    {
        { "CGlobalSymbol", "GlobalSymbol" },
        { "CUtlStringToken", "GlobalSymbol" },
        { "CTransform", "Transform" },

        { "ParticleAttachment_t", "ValveResourceFormat.Particles.ParticleAttachment" },
        { "CPiecewiseCurve", "ValveResourceFormat.Particles.Utils.PiecewiseCurve" },

        // todo
        { "CStrongHandle", "string" },
        { "CStrongHandleVoid", "string" },
    };

    public override string ChooseFolder(Converter converter, string fileName, string outputDir)
    {
        fileName = fileName.Split("__", StringSplitOptions.RemoveEmptyEntries)[0];

        if (converter.IsEnum(fileName))
        {
            outputDir += "/Enums";
        }

        if (FolderByRootClass.TryGetValue(converter.RootClass(fileName), out var folder))
        {
            outputDir += $"/{folder}";
        }

        return outputDir;
    }

    public override string ClassKeywords(Converter converter, string className)
        => className is "BitFlags" ? "readonly partial struct" : "partial class";

    // CNmVelocityBlendNode::CDefinition is the VelocityBlendNode
    public override string PostProcessClassName(string className)
        => className.Replace("__Definition", "", StringComparison.Ordinal);

    public override void Test(Converter converter)
    {
        Span<(string, string)> classNameTests = [
            ("CNmVelocityBlendNode::CDefinition", "VelocityBlendNode"),
            ("CNmStateNode::TimedEvent_t::Comparison_t", "StateNode__TimedEvent__Comparison"),
            ("NmEasingOperation_t", "EasingOperation"),
            ("CUtlString", "string"),
        ];

        foreach (var (input, expected) in classNameTests)
        {
            var actual = converter.ConvertClassName(input, SourceNameSpace);
            Debug.Assert(actual == expected, $"Expected '{expected}', got '{actual}'");
        }
    }
}
