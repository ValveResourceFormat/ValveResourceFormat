using ValveResourceFormat.IO.Fgd;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Makes an entity a candidate for the globals its class declares, such as <c>fog</c>, <c>tonemap</c> or
/// <c>gradient_fog</c>. Of the entities that can be a global, one is: the first that is on, at the fewest
/// prefabs deep.
/// </summary>
public sealed class GlobalHelper : EntityHelper
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
        => EnvironmentKeys.ParseInteger(GetStringKey("startdisabled"), 0) == 0 && EnvironmentKeys.ParseInteger(GetStringKey("fogenable"), 1) != 0;

    /// <summary>Initializes the globals of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public GlobalHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }
}
