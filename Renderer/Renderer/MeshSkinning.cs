namespace ValveResourceFormat.Renderer;

/// <summary>
/// How many bones a vertex blends. Doubles as the <c>D_SKINNING</c> combo value.
/// </summary>
public enum MeshSkinning : byte
{
    /// <summary>No blend attributes.</summary>
    None,
    /// <summary>One bone, stored as an index with no weight.</summary>
    OneBone,
    /// <summary>Up to four weighted bones.</summary>
    FourBones,
    /// <summary>Up to eight weighted bones.</summary>
    EightBones,
}
