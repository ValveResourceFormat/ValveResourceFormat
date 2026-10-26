using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A shape drawn at a point one key of an entity holds, while the entity is selected and, where the class asks
/// for that, while it is not.
/// </summary>
internal abstract class PointKeyHelper : EntityHelper
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

    /// <summary>Gets whether the point is in the space of the entity, rather than in world space.</summary>
    private bool IsLocal { get; }

    /// <summary>Gets whether the shape is drawn while the entity is not selected too.</summary>
    private bool DrawUnselected { get; }

    /// <summary>Gets whether nothing is drawn while a point in the space of the entity is its origin.</summary>
    private bool HideIfZero { get; }

    /// <summary>Gets whether the shape is only drawn while the model of the entity can break into pieces.</summary>
    private bool RequiresBreakableModel { get; }

    /// <summary>Initializes a shape at a point of an entity.</summary>
    /// <param name="owner">The entity the helper belongs to.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <param name="isLocal">Whether the point is in the space of the entity, rather than in world space.</param>
    private protected PointKeyHelper(IHelperOwner owner, HelperDeclaration declaration, bool isLocal) : base(owner, declaration)
    {
        PointKey = GetArgument(0)?.ToLowerInvariant() ?? DefaultPointKey;
        IsLocal = isLocal;
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

    /// <summary>Gets the point in the world the shape is drawn at.</summary>
    /// <param name="state">Whether the entity is selected.</param>
    /// <returns>The point, or <see langword="null"/> when nothing is drawn.</returns>
    private protected Vector3? GetPoint(in HelperRenderState state)
    {
        if (!state.IsSelected && !DrawUnselected)
        {
            return null;
        }

        if (ReadPoint() is not { } point || (RequiresBreakableModel && !(modelIsBreakable ??= HasBreakableModel())))
        {
            return null;
        }

        return point;
    }

    // Until the key holds all three numbers the point is the origin of the entity, as a point in its space
    private Vector3? ReadPoint()
    {
        var isRead = KeyValueScanner.TryReadVector(GetStringKey(PointKey), out var point);

        if (isRead && !IsLocal)
        {
            return point;
        }

        if (IsLocal && HideIfZero && point == Vector3.Zero)
        {
            return null;
        }

        return Vector3.Transform(point, Owner.RigidTransform);
    }

    // Whether the first model of the entity has a list of what it breaks into
    private bool HasBreakableModel()
    {
        if (Owner.Helpers.OfType<ModelHelper>().FirstOrDefault() is not { ModelName: { } modelName })
        {
            return false;
        }

        return Owner.Scene.RendererContext.FileLoader.LoadFileCompiled(modelName)?.DataBlock is Model model
            && model.KeyValues.ContainsKey("break_list");
    }
}
