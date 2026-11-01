using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_shatterglass</c>. A pane of glass that can shatter. Drawn here as the intact pane.
/// </summary>
/// <remarks>
/// The compiled panel model carries the material of a broken pane, whose normal map is the whole crack
/// pattern. The game hides that model and draws the intact pane with the undamaged material instead.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CFuncShatterglass">CFuncShatterglass</seealso>
public sealed class FuncShatterglass : BaseModelEntity
{
    private const string UndamagedMaterial = "materials/glass/shatterglass_undamaged.vmat";

    /// <summary>Initializes a <c>func_shatterglass</c> from its keyvalues.</summary>
    public FuncShatterglass(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool BuildsCollider => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        if (ModelNode is not { } node || KeyValues.GetBooleanProperty("startbroken"))
        {
            return;
        }

        foreach (var mesh in node.AllRenderableMeshes)
        {
            foreach (var drawCall in mesh.DrawCalls)
            {
                mesh.ReplaceMaterial(drawCall, UndamagedMaterial);
            }
        }
    }
}
