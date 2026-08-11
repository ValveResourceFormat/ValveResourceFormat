using System.Diagnostics;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Replays the engine's vpcf KV3 format-conversion chain, upgrading particle system documents
/// from their stored format to the newest implemented format.
/// </summary>
public static class ParticleFormatUpgrader
{
    /// <summary>
    /// The newest <c>m_nBehaviorVersion</c> the chain produces.
    /// </summary>
    public const int LatestBehaviorVersion = 12;

    internal static IReadOnlyList<ParticleUpgradeStep> Steps { get; } =
    [
        new GenericToVpcf1(),
    ];

    private static readonly Guid[] ChainIds = BuildChainIds();

    /// <summary>
    /// The newest format the chain produces. Definitions built in memory should declare this so
    /// the chain does not replay over content that is already current.
    /// </summary>
    public static KV3ID LatestFormat { get; } = new(Steps[^1].ToFormat, ChainIds[^1]);

    /// <summary>
    /// Maps each chain position to the format id a document must carry to enter there, taken from
    /// the steps themselves so the list order and the format names cannot drift apart.
    /// </summary>
    private static Guid[] BuildChainIds()
    {
        var ids = new Guid[Steps.Count + 1];

        for (var i = 0; i < Steps.Count; i++)
        {
            Debug.Assert(i == 0 || Steps[i - 1].ToFormat == Steps[i].FromFormat,
                $"Chain break: {Steps[i - 1].ToFormat} is followed by a step consuming {Steps[i].FromFormat}.");

            ids[i] = KV3IDLookup.Table[Steps[i].FromFormat];
        }

        ids[Steps.Count] = KV3IDLookup.Table[Steps[^1].ToFormat];

        return ids;
    }

    /// <summary>
    /// Deep-clones the given document root and applies every implemented chain step past the
    /// stored format, returning the upgraded clone. Missing and unknown formats start at the
    /// oldest step, matching the engine treating headerless data as oldest. A stored format
    /// newer than the implemented steps returns the root unchanged.
    /// </summary>
    public static KVObject UpgradeToLatest(KVObject root, KV3ID? storedFormat)
    {
        ArgumentNullException.ThrowIfNull(root);

        var start = ResolveStartIndex(storedFormat);

        if (start >= Steps.Count)
        {
            return root;
        }

        var upgraded = KVObjectDeepClone.Clone(root);
        ApplyFrom(upgraded, start);

        return upgraded;
    }

    /// <summary>
    /// Runs every step from <paramref name="startIndex"/> on, in place.
    /// </summary>
    internal static void ApplyFrom(KVObject clone, int startIndex)
    {
        for (var i = startIndex; i < Steps.Count; i++)
        {
            Steps[i].Apply(clone);
        }
    }

    internal static int ResolveStartIndex(KV3ID? storedFormat)
    {
        if (storedFormat is not { } format)
        {
            return 0;
        }

        var index = Array.IndexOf(ChainIds, format.Id);
        return index < 0 ? 0 : index;
    }
}
