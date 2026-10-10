namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What an entity can do, as returned by <see cref="BaseEntity.ObjectCaps"/>. Only the use-related
/// capabilities so far, with the game's flag values.
/// </summary>
[Flags]
public enum EntityCapability : uint
{
    /// <summary>Cannot be interacted with.</summary>
    None = 0,

    /// <summary>Responds to a single press of use.</summary>
    ImpulseUse = 0x00000010,

    /// <summary>Responds for as long as use is held.</summary>
    ContinuousUse = 0x00000020,

    /// <summary>Responds to use being pressed and again to it being released.</summary>
    OnOffUse = 0x00000040,

    /// <summary>Only responds to use from the direction it faces.</summary>
    DirectionalUse = 0x00000080,

    /// <summary>Any capability that makes an entity a candidate for the player's use trace.</summary>
    UsableMask = ImpulseUse | ContinuousUse | OnOffUse | DirectionalUse,
}
