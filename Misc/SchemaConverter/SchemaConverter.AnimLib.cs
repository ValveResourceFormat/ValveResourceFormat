using System.Diagnostics;

/// <summary>
/// The animation graph definitions in Renderer/Animation/AnimLib/Definitions.
/// </summary>
sealed class AnimLibTarget : SchemaTarget
{
    private static readonly Dictionary<string, string> FileByRootClass = new()
    {
        { "GraphNode", "Nodes" },
        { "Event", "Events" },
        { "PoseTask", "Tasks" },
    };

    // Small definitions that belong together share a file
    private static readonly Dictionary<string, string> FileByClass = new()
    {
        { "BoneMaskSetDefinition", "BoneMasks" },
        { "BoneWeightList", "BoneMasks" },
        { "ContactAudioActionVData", "ContactAudio" },
        { "ContactAudioInfo", "ContactAudio" },
        { "ContactAudioTypeVData", "ContactAudio" },
        { "FloatChannelData", "FloatChannels" },
        { "FloatChannelSet", "FloatChannels" },
        { "FloatCurveCompressionSettings", "FloatChannels" },
        { "SyncTrackTime", "SyncTrack" },
        { "SyncTrackTimeRange", "SyncTrack" },
    };

    public override string Name => "animlib";

    public override string DestinationDir => Path.Combine("Renderer", "Animation", "AnimLib", "Definitions");

    public override string DestinationNameSpace => "ValveResourceFormat.Renderer.AnimLib";

    public override string BuildProject => "Renderer";

    public override string SourceNameSpace => "Nm";

    // Only the graph system's own classes: editor document classes are not part of the compiled resources,
    // and client module nodes are defined alongside the client code that registers them. The graph instance
    // and variation user data have nothing in them to read.
    public override bool IsIncluded(string module, string name)
        => module == "animlib"
            && !name.Contains("Doc", StringComparison.Ordinal)
            && name is not ("CNmGraphInstance" or "CNmGraphVariationUserData");

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

    // Nodes, events, tasks and enums are many small definitions, kept in one file per kind. Everything
    // else goes in the file of the type it is nested in.
    public override string ChooseFile(Converter converter, string internalName)
    {
        var outermost = internalName.Split("__", StringSplitOptions.RemoveEmptyEntries)[0];

        if (converter.IsEnum(outermost))
        {
            return "Enums";
        }

        return FileByRootClass.GetValueOrDefault(converter.RootClass(outermost))
            ?? FileByClass.GetValueOrDefault(outermost)
            ?? base.ChooseFile(converter, outermost);
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
