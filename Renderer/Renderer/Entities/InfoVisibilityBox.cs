using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_visibility_box</c>. While enabled, hides the scene objects of its scene by their bounds: those
/// entirely inside it, or those outside it, as its <c>cull_mode</c> says.
/// </summary>
/// <remarks>
/// The box is placed where the entity is when it is enabled, and stays there until it is disabled.
/// Counter-Strike 2 has the camera-inside mode 2; the other games treat every nonzero mode as mode 1.
/// </remarks>
public sealed class InfoVisibilityBox : BaseEntity
{
    private const string CameraInsideModeGame = "Counter-Strike 2";

    /// <summary>Gets the volume this entity culls with.</summary>
    public VisibilityBox Box { get; private set; } = null!;

    /// <summary>Initializes an <c>info_visibility_box</c> from its keyvalues.</summary>
    public InfoVisibilityBox(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var cullMode = KeyValues.GetInt32Property("cull_mode");
        var gameName = EntitySystem.RendererContext.FileLoader.GameName;
        var hasCameraInsideMode = gameName == null || gameName == CameraInsideModeGame;

        var mode = cullMode switch
        {
            0 => VisibilityBoxMode.Inside,
            2 when hasCameraInsideMode => VisibilityBoxMode.OutsideWhenEyeInside,
            1 or 2 => VisibilityBoxMode.Outside,
            _ => hasCameraInsideMode ? VisibilityBoxMode.Inside : VisibilityBoxMode.Outside,
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
