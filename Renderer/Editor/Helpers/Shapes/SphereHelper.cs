using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A sphere around a selected entity whose radius is one of its keys, such as how far a sound carries. It
/// stays aligned to the world axes, and a class can have it drawn while the entity is not selected too.
/// </summary>
public sealed class SphereHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const string DefaultRadiusKey = "radius";

    private static readonly Color32 DefaultUnselectedColor = new(128, 128, 128);

    /// <summary>Gets the key of the entity that holds the radius.</summary>
    public string RadiusKey { get; }

    /// <summary>Gets the key of the entity that holds how far in from the radius its influence fades, empty when there is none.</summary>
    public string EdgeFadeKey { get; }

    /// <summary>
    /// Gets the key of the entity that holds the center of the sphere in world space, or <see langword="null"/>
    /// when it is the origin of the entity.
    /// </summary>
    public string? OriginKey { get; }

    /// <summary>Gets the color of the sphere while its entity is selected.</summary>
    public Color32 Color { get; }

    /// <summary>Gets the color of the sphere while its entity is not selected.</summary>
    public Color32 UnselectedColor { get; }

    /// <summary>Gets whether the sphere is drawn as three circles rather than rings and meridians.</summary>
    public bool IsLean { get; }

    /// <summary>Gets whether the sphere is only drawn while its entity is the only thing selected.</summary>
    public bool SingleSelect { get; }

    /// <summary>Gets whether the sphere is drawn while its entity is not selected too.</summary>
    public bool AlwaysRender { get; }

    /// <summary>Initializes the sphere a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public SphereHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        IsLean = declaration.Name.Equals("leansphere", StringComparison.OrdinalIgnoreCase);

        if (declaration.Metadata != null)
        {
            RadiusKey = (GetMetadata("radius") ?? DefaultRadiusKey).ToLowerInvariant();
            EdgeFadeKey = GetMetadata("edge_fade")?.ToLowerInvariant() ?? string.Empty;
            Color = GetMetadataColor("color") ?? Color32.White;
            UnselectedColor = GetMetadataColor("unselectedColor") ?? DefaultUnselectedColor;
            SingleSelect = GetMetadataFlag("singleSelect", false);
            AlwaysRender = GetMetadataFlag("alwaysRender", false);
            return;
        }

        RadiusKey = (GetArgument(0) ?? DefaultRadiusKey).ToLowerInvariant();
        EdgeFadeKey = string.Empty;
        Color = GetColorArguments(1);
        UnselectedColor = DefaultUnselectedColor;

        // Flags follow the color, and the key of the center comes two after its flag
        for (var i = 4; i < declaration.Arguments.Count; i++)
        {
            var argument = declaration.Arguments[i];

            if (argument.Equals("singleSelect", StringComparison.OrdinalIgnoreCase))
            {
                SingleSelect = true;
            }
            else if (argument.Equals("alwaysRender", StringComparison.OrdinalIgnoreCase))
            {
                AlwaysRender = true;
            }
            else if (argument.Equals("originKey", StringComparison.OrdinalIgnoreCase))
            {
                OriginKey = GetArgument(i + 2)?.ToLowerInvariant();
                i += 2;
            }
        }
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (AlwaysRender)
        {
            AddShapes();
        }
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var radius = GetFloatKey(RadiusKey);

        // A lean sphere is drawn however much else is selected
        var shown = state.IsSelected ? IsLean || !SingleSelect || state.IsOnlySelected : AlwaysRender;

        if (radius <= 0f || !shown)
        {
            return;
        }

        var edgeFade = EdgeFadeKey.Length > 0 ? GetFloatKey(EdgeFadeKey) : 0f;

        // The edge fade is a fainter inner sphere in the selected color
        var innerRadius = MathF.Max(radius - edgeFade, 0f);
        var innerColor = Color with { A = (byte)(Color.A >> 1) };

        context.DrawColor = state.IsSelected ? Color : UnselectedColor;

        if (IsLean)
        {
            var origin = Owner.RigidTransform.Translation;

            context.DrawSphereCircles(origin, radius, 48);

            if (EdgeFadeKey.Length > 0 && edgeFade > 0f)
            {
                context.DrawColor = innerColor;
                context.DrawSphereCircles(origin, innerRadius, 48);
            }

            return;
        }

        // The key, or the default its class gives it, names a point in the world once it holds one
        var center = OriginKey != null && KeyValueScanner.TryReadVector(GetStringKey(OriginKey), out var originKeyValue)
            ? originKeyValue
            : Owner.RigidTransform.Translation;

        context.DrawWireSphere(center, radius, 12, 12);

        if (EdgeFadeKey.Length > 0)
        {
            context.DrawColor = innerColor;
            context.DrawWireSphere(center, innerRadius, 12, 12);
        }
    }
}
