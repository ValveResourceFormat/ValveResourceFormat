using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The volume a selected fog controller covers, dashed over the scene, with the volume of every fog volume of
/// its map: the box around all of them, or the box of the controller itself while the map has none.
/// </summary>
public sealed class VolumetricFogControllerHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    private const string FogVolumeClassname = "env_volumetric_fog_volume";

    /// <summary>Initializes the volumes of a fog controller.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public VolumetricFogControllerHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var volumes = Owner.FindByClass(FogVolumeClassname).ToList();

        // The controller covers every fog volume of the map wherever it is placed, or its own box while there
        // are none, along the world axes either way
        AABB covered;

        if (volumes.Count == 0)
        {
            var origin = Owner.RigidTransform.Translation;
            covered = new AABB(GetVector3Key("box_mins") + origin, GetVector3Key("box_maxs") + origin);
        }
        else
        {
            covered = GetVolumeBounds(volumes[0]).Transform(volumes[0].RigidTransform);

            foreach (var volume in volumes)
            {
                covered = covered.Union(GetVolumeBounds(volume).Transform(volume.RigidTransform));
            }
        }

        context.PushRenderMode(ToolRenderMode.DottedNoZ);
        context.DrawColor = GetDrawColor(state) with { A = 255 };
        context.DrawWireBox(covered.Min, covered.Max);
        context.PopRenderMode();

        context.DrawColor = Color32.White;

        foreach (var volume in volumes)
        {
            var bounds = GetVolumeBounds(volume);

            context.PushTransform(volume.RigidTransform);
            context.PushRenderMode(ToolRenderMode.WireframeNoZ);
            context.DrawWireBox(bounds.Min, bounds.Max);
            context.PopRenderMode();
            context.PopTransform();
        }
    }

    private static AABB GetVolumeBounds(IHelperOwner volume)
        => new(volume.KeyValues.GetVector3Property("box_mins"), volume.KeyValues.GetVector3Property("box_maxs"));
}
