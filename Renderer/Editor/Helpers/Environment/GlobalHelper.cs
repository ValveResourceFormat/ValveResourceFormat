using ValveResourceFormat.IO.Fgd;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Makes an entity a candidate for the globals its class declares, such as <c>fog</c>, <c>tonemap</c> or
/// <c>gradient_fog</c>. Of the entities that can be a global, the first one that is on and has the
/// fewest prefab levels is the global.
/// </summary>
internal sealed class GlobalHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Gets the names of the globals the entity can be.</summary>
    public IReadOnlyList<string> Names => Declaration.Arguments;

    /// <summary>
    /// Gets whether the entity is on, so it can be a global: neither disabled at the start nor, for a fog,
    /// with its fog turned off.
    /// </summary>
    public bool IsEnabled
        => !GetBooleanKey("startdisabled") && GetBooleanKey("fogenable", true);
}
