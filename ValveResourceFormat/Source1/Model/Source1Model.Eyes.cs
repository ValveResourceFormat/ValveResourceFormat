namespace ValveResourceFormat.Source1.Model;

/// <summary>An eyeball of a Source 1 sub-model.</summary>
internal sealed class Source1Eyeball
{
    /// <summary>Index of the bone the eyeball follows.</summary>
    public required int Bone { get; init; }

    /// <summary>Center of the eyeball in the space of the bone.</summary>
    public required Vector3 Origin { get; init; }

    /// <summary>Sideways turn of the eye away from its target, in units of the radius.</summary>
    public required float ZOffset { get; init; }

    /// <summary>Radius of the eyeball.</summary>
    public required float Radius { get; init; }

    /// <summary>Up direction of the eyeball in the space of the bone.</summary>
    public required Vector3 Up { get; init; }

    /// <summary>Forward direction of the eyeball in the space of the bone.</summary>
    public required Vector3 Forward { get; init; }

    /// <summary>Size of the iris relative to the eyeball.</summary>
    public required float IrisScale { get; init; }

    /// <summary>Whether the eyelids are driven by flex controllers only, instead of following the aim of the eye.</summary>
    public required bool NonFacs { get; init; }

    /// <summary>Flexes the raised, neutral and lowered controls of the upper lid set.</summary>
    public required int[] UpperFlexes { get; init; }

    /// <summary>Flexes the raised, neutral and lowered controls of the lower lid set.</summary>
    public required int[] LowerFlexes { get; init; }

    /// <summary>Positions of the upper lid when raised, neutral and lowered, as offsets on the eyeball.</summary>
    public required float[] UpperTargets { get; init; }

    /// <summary>Positions of the lower lid when raised, neutral and lowered, as offsets on the eyeball.</summary>
    public required float[] LowerTargets { get; init; }

    /// <summary>Flex that moves the vertices of the upper lid to follow the eye.</summary>
    public required int UpperLidFlex { get; init; }

    /// <summary>Flex that moves the vertices of the lower lid to follow the eye.</summary>
    public required int LowerLidFlex { get; init; }
}

internal sealed partial class Source1Model
{
    /// <summary>Cosine of the largest angle the eyes may turn away from the forward direction of the eyes attachment.</summary>
    public float MaxEyeDeflection { get; private set; }

    private static Source1Eyeball[][][] ConvertEyeballs(MdlFile mdl)
    {
        var parts = new Source1Eyeball[mdl.Eyeballs.Length][][];

        for (var part = 0; part < parts.Length; part++)
        {
            parts[part] = new Source1Eyeball[mdl.Eyeballs[part].Length][];

            for (var model = 0; model < parts[part].Length; model++)
            {
                var eyeballs = mdl.Eyeballs[part][model];
                parts[part][model] = new Source1Eyeball[eyeballs.Length];

                for (var i = 0; i < eyeballs.Length; i++)
                {
                    var eyeball = eyeballs[i];

                    parts[part][model][i] = new Source1Eyeball
                    {
                        Bone = eyeball.Bone,
                        Origin = eyeball.Origin,
                        ZOffset = eyeball.ZOffset,
                        Radius = eyeball.Radius,
                        Up = eyeball.Up,
                        Forward = eyeball.Forward,
                        IrisScale = eyeball.IrisScale,
                        NonFacs = eyeball.NonFacs != 0,
                        UpperFlexes = eyeball.UpperFlexDesc,
                        LowerFlexes = eyeball.LowerFlexDesc,
                        UpperTargets = eyeball.UpperTarget,
                        LowerTargets = eyeball.LowerTarget,
                        UpperLidFlex = eyeball.UpperLidFlexDesc,
                        LowerLidFlex = eyeball.LowerLidFlexDesc,
                    };
                }
            }
        }

        return parts;
    }
}
