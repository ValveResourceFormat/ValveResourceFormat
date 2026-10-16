using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The box an entity is drawn as: the size its class declares, or a small one where nothing else of the
/// entity is seen. It sits at the entity in the color of its class and does not turn with it.
/// </summary>
public sealed class AlignedBoxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets the smallest corner of the box, from the origin of the entity.</summary>
    public Vector3 Mins { get; }

    /// <summary>Gets the largest corner of the box, from the origin of the entity.</summary>
    public Vector3 Maxs { get; }

    /// <summary>Initializes the box of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="mins">Smallest corner of the box, from the origin of the entity.</param>
    /// <param name="maxs">Largest corner of the box, from the origin of the entity.</param>
    public AlignedBoxHelper(IHelperOwner owner, Vector3 mins, Vector3 maxs)
        : base(owner, new FgdHelper { Name = "wirebox" })
    {
        Mins = mins;
        Maxs = maxs;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var color = Owner.FgdClass?.Color ?? FgdClass.DefaultColor;
        var center = Owner.Transform.Translation + ((Mins + Maxs) / 2f);

        Owner.AddNode(new SimpleBoxSceneNode(Owner.Scene, color, Maxs - Mins)
        {
            Transform = Matrix4x4.CreateTranslation(center),
            LayerName = HammerEntityVisuals.MarkerLayerName,
            KeepsMarkerSize = true,
        });
    }
}
