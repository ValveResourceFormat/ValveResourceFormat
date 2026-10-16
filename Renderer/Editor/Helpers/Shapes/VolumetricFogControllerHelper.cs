using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The volume a selected fog controller covers, with the volume of every fog volume of its map: the box
/// around all of them, or the box of the controller itself while the map has none.
/// </summary>
public sealed class VolumetricFogControllerHelper : EntityHelper
{
    private const string FogVolumeClassname = "env_volumetric_fog_volume";

    /// <summary>Initializes the volumes of a fog controller.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public VolumetricFogControllerHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var volumes = Owner.FindByClass(FogVolumeClassname).ToList();
        var color = EntityHelperLines.SelectionColor;

        if (volumes.Count == 0)
        {
            var bounds = new AABB(GetVector3Key("box_mins"), GetVector3Key("box_maxs"));
            shapes.Aligned(HelperPasses.Overlay).AddBox(bounds, color);
            return;
        }

        // The controller covers every fog volume of the map, wherever it is placed
        AABB? covered = null;

        foreach (var volume in volumes)
        {
            var bounds = GetVolumeBounds(volume);
            var worldBounds = bounds.Transform(volume.RigidTransform);

            covered = covered is { } union ? union.Union(worldBounds) : worldBounds;

            new EntityHelperLines(shapes.Output, volume.RigidTransform * volume.Scene.ToViewerWorld, HelperPasses.Overlay).AddBox(bounds, Color32.White);
        }

        shapes.World(HelperPasses.Overlay).AddBox(covered!.Value, color);
    }

    private static AABB GetVolumeBounds(IHelperOwner volume)
        => new(volume.KeyValues.GetVector3Property("box_mins"), volume.KeyValues.GetVector3Property("box_maxs"));
}
