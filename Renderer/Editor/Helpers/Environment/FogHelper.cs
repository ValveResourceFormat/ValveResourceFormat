using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A fog entity. A map is drawn with the fog of one of them, which <see cref="Apply"/> sets.
/// </summary>
public abstract class FogHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets whether the fog is off until something in the game turns it on.</summary>
    public bool StartsDisabled => Owner.KeyValues.GetBooleanProperty("startdisabled");

    /// <summary>Initializes the fog of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    protected FogHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <summary>Makes this fog the one the scene of the entity is drawn with.</summary>
    public abstract void Apply();
}
