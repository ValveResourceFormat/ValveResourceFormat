namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_physical_button</c>. A <c>func_button</c> that a hand pokes in. There are no hands here, so use
/// always presses it instead, whatever its spawnflags.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPhysicalButton">CPhysicalButton</seealso>
public sealed class FuncPhysicalButton : FuncButton
{
    /// <inheritdoc/>
    protected override bool IsUseActivated => true;

    /// <summary>Initializes a <c>func_physical_button</c> from its keyvalues.</summary>
    public FuncPhysicalButton(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}
