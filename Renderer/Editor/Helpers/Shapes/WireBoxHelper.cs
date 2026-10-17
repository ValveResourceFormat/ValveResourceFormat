using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The edges of a box two keys of an entity give, drawn whether or not the entity is selected: in world
/// coordinates, or from the origin of the entity along the world axes.
/// </summary>
internal sealed class WireBoxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    /// <summary>Gets the key of the entity that holds the smallest corner.</summary>
    private string MinsKey { get; }

    /// <summary>Gets the key of the entity that holds the largest corner.</summary>
    private string MaxsKey { get; }

    /// <summary>Gets whether the corners are in world coordinates, rather than from the origin of the entity.</summary>
    private bool IsWorld { get; }

    private WireBoxHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        MinsKey = ToKey(declaration.Arguments[0]);
        MaxsKey = ToKey(declaration.Arguments[1]);
        IsWorld = declaration.Name.Equals("wirebox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the box a class declares.</summary>
    /// <returns>The box, or <see langword="null"/> when the declaration does not name both keys.</returns>
    public static WireBoxHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count >= 2 ? new WireBoxHelper(owner, declaration) : null;
    }

    // The editor keeps no more than 31 characters of each key
    private static string ToKey(string argument) => argument[..Math.Min(argument.Length, 31)].ToLowerInvariant();

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes();

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var offset = IsWorld ? Vector3.Zero : Owner.RigidTransform.Translation;

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = GetDrawColor(state) with { A = 255 };
        context.DrawWireBox(GetVector3Key(MinsKey) + offset, GetVector3Key(MaxsKey) + offset);
        context.PopRenderMode();
    }
}
