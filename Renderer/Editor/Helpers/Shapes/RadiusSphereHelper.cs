using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The sphere an occluder hides what is behind with, or the one a viewer sees within, around the entity as
/// far as one of its keys. It is drawn whether or not the entity is selected, red for an occluder and green
/// for a viewer, whatever color the declaration gives.
/// </summary>
public sealed class RadiusSphereHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    private const string DefaultRadiusKey = "radius";

    private static readonly Color32 OccluderColor = new(255, 0, 0);
    private static readonly Color32 ViewerColor = new(0, 255, 0);

    /// <summary>Gets the key of the entity that holds the radius.</summary>
    public string RadiusKey { get; }

    /// <summary>Gets the color of the sphere.</summary>
    public Color32 Color { get; }

    /// <summary>Initializes the sphere a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public RadiusSphereHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        RadiusKey = GetArgument(0)?.ToLowerInvariant() ?? DefaultRadiusKey;
        Color = declaration.Name.Equals("viewer", StringComparison.OrdinalIgnoreCase) ? ViewerColor : OccluderColor;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        AddShapes();
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var radius = GetFloatKey(RadiusKey);

        if (radius <= 0f)
        {
            return;
        }

        context.DrawWireSphere(Owner.RigidTransform.Translation, radius, 12, 12, Color);
    }
}
