using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A point one key of an entity holds, marked by a handle and joined to the entity by a line, the point in
/// world space or in the space of the entity. It is drawn while the entity is selected, the line dashed where
/// the scene hides it, and faintly while it is not where the class asks for that.
/// </summary>
internal sealed class VecLineHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const string DefaultPointKey = "origin";

    private bool? modelIsBreakable;

    /// <summary>Gets the key of the entity that holds the point.</summary>
    private string PointKey { get; }

    /// <summary>Gets the color of the line.</summary>
    private Color32 Color { get; }

    /// <summary>Gets whether the point is in the space of the entity, rather than in world space.</summary>
    private bool IsLocal { get; }

    /// <summary>Gets whether the line is drawn while the entity is not selected too.</summary>
    private bool DrawUnselected { get; }

    /// <summary>Gets whether no line is drawn while a point in the space of the entity is its origin.</summary>
    private bool HideIfZero { get; }

    /// <summary>Gets whether the line is only drawn while the model of the entity can break into pieces.</summary>
    private bool RequiresBreakableModel { get; }

    public VecLineHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        PointKey = GetArgument(0)?.ToLowerInvariant() ?? DefaultPointKey;
        Color = declaration.Arguments.Count >= 4 ? GetColorArguments(1) : Color32.White;
        IsLocal = declaration.Name.Equals("vecline_local", StringComparison.OrdinalIgnoreCase);
        DrawUnselected = HasArgument("draw_unselected");
        HideIfZero = HasArgument("hideifzero");
        RequiresBreakableModel = HasArgument("requires_breakable_model");
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (DrawUnselected)
        {
            AddShapes();
        }
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected && !DrawUnselected)
        {
            return;
        }

        if (GetPoint(Owner, GetStringKey(PointKey), IsLocal, HideIfZero) is not { } point
            || (RequiresBreakableModel && !(modelIsBreakable ??= HasBreakableModel(Owner))))
        {
            return;
        }

        var origin = Owner.RigidTransform.Translation;
        var color = Color with { A = state.IsSelected ? byte.MaxValue : (byte)128 };

        // The point is a handle, over everything while the entity is selected
        context.LayerOverride = state.IsSelected ? ToolRenderLayer.Overlay0 : null;
        context.DrawHandle(10f, ToolHandleType.Circle, color, point, noZ: state.IsSelected, border: false, translucent: true);
        context.LayerOverride = null;

        if (!state.IsSelected)
        {
            context.PushRenderMode(ToolRenderMode.WireframeTranslucent);
            context.DrawLine(point, origin, color);
            context.PopRenderMode();
            return;
        }

        // Where the scene hides it, the line is dashed over it
        context.LineWidth = 2f;

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawLine(point, origin, color);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.DottedNoZ);
        context.DrawLine(point, origin, color);
        context.PopRenderMode();

        context.LineWidth = 0f;
    }

    /// <summary>
    /// Gets the point the key of a point helper holds, in the world. Until the key holds all three numbers the
    /// point is the origin of the entity, as a point in its space.
    /// </summary>
    /// <param name="owner">The entity.</param>
    /// <param name="value">The value of the key.</param>
    /// <param name="isLocal">Whether the point is in the space of the entity, rather than in world space.</param>
    /// <param name="hideIfZero">Whether a point in the space of the entity is left out at its origin.</param>
    /// <returns>The point, or <see langword="null"/> when it is left out.</returns>
    internal static Vector3? GetPoint(IHelperOwner owner, string value, bool isLocal, bool hideIfZero)
    {
        var isRead = KeyValueScanner.TryReadVector(value, out var point);

        if (isRead && !isLocal)
        {
            return point;
        }

        if (isLocal && hideIfZero && point == Vector3.Zero)
        {
            return null;
        }

        return Vector3.Transform(point, owner.RigidTransform);
    }

    /// <summary>Gets whether the first model of an entity can break into pieces.</summary>
    /// <param name="owner">The entity.</param>
    /// <returns>Whether it has a model, and the model has a list of what it breaks into.</returns>
    internal static bool HasBreakableModel(IHelperOwner owner)
    {
        if (owner.Helpers.OfType<ModelHelper>().FirstOrDefault() is not { ModelName: { } modelName })
        {
            return false;
        }

        return owner.Scene.RendererContext.FileLoader.LoadFileCompiled(modelName)?.DataBlock is Model model
            && model.KeyValues.ContainsKey("break_list");
    }
}
