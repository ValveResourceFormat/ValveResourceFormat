using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A capsule along +X of a selected entity, from its origin out as far as its <c>capsule_height</c> key and
/// as thick as its <c>capsule_radius</c> key.
/// </summary>
public sealed class CapsuleHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const int Segments = 8;

    private static readonly Color32 Color = new(0, 255, 255);

    /// <summary>Initializes the capsule a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public CapsuleHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var height = GetFloatKey("capsule_height");
        var radius = GetFloatKey("capsule_radius");

        context.PushTransform(Owner.RigidTransform);
        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawSolidCapsule(Vector3.Zero, new Vector3(height, 0f, 0f), radius, Segments, Color);
        context.PopRenderMode();
        context.PopTransform();
    }
}
