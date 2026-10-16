using ValveResourceFormat.IO.Fgd;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The edges of a box two keys of an entity give, drawn whether or not the entity is selected: in world
/// coordinates, or from the origin of the entity along the world axes.
/// </summary>
public sealed class WireBoxHelper : EntityHelper
{
    /// <summary>Gets the key of the entity that holds the smallest corner.</summary>
    public string MinsKey { get; }

    /// <summary>Gets the key of the entity that holds the largest corner.</summary>
    public string MaxsKey { get; }

    /// <summary>Gets whether the corners are in world coordinates, rather than from the origin of the entity.</summary>
    public bool IsWorld { get; }

    private WireBoxHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        MinsKey = declaration.Arguments[0].ToLowerInvariant();
        MaxsKey = declaration.Arguments[1].ToLowerInvariant();
        IsWorld = declaration.Name.Equals("wirebox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the box a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>The box, or <see langword="null"/> when the declaration does not name both keys.</returns>
    public static WireBoxHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count >= 2 ? new WireBoxHelper(owner, declaration) : null;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var bounds = GetBounds();
        var color = ClassColor;

        AddLines(GetPlacement(), lines => lines.AddBox(bounds, color));
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        shapes.World(HelperPasses.Visible).Transformed(GetPlacement()).AddBox(GetBounds(), EntityHelperLines.SelectionColor);
    }

    private AABB GetBounds() => new(GetVector3Key(MinsKey), GetVector3Key(MaxsKey));

    private Matrix4x4 GetPlacement()
        => IsWorld ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(Owner.RigidTransform.Translation);
}
