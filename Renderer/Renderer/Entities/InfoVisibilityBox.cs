using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_visibility_box</c>. While enabled, hides the scene objects of its scene by their bounds: those
/// entirely inside it, or those outside it, as its <c>cull_mode</c> says.
/// </summary>
/// <remarks>
/// The box is placed where the entity is when it is enabled, and stays there until it is disabled.
/// </remarks>
public sealed class InfoVisibilityBox : BaseEntity
{
    /// <summary>Gets the volume this entity culls with.</summary>
    public VisibilityBox Box { get; private set; } = null!;

    /// <summary>Initializes an <c>info_visibility_box</c> from its keyvalues.</summary>
    public InfoVisibilityBox(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var mode = KeyValues.GetInt32Property("cull_mode") switch
        {
            1 => VisibilityBoxMode.Outside,
            2 => VisibilityBoxMode.OutsideWhenEyeInside,
            _ => VisibilityBoxMode.Inside,
        };

        Box = new VisibilityBox(mode, KeyValues.GetVector3Property("box_size", new Vector3(128f)));
        Scene.VisibilityBoxes.Add(Box);

        if (!KeyValues.GetBooleanProperty("startdisabled"))
        {
            Box.Enable(RigidTransform);
        }
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => Box.Enable(RigidTransform);

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => Box.Disable();

    /// <inheritdoc/>
    protected override void OnRemove() => Scene.VisibilityBoxes.Remove(Box);
}
