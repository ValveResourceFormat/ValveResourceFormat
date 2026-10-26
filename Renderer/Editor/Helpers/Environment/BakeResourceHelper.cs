using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What an entity gets baked for it when the map is built: the light probe of a light probe volume, or the
/// cubemap of a cubemap, which the scene is lit with.
/// </summary>
public sealed class BakeResourceHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    private const string LightProbeVolume = "ToolObjects/LightProbeVolume";
    private const string CubeMap = "ToolObjects/CubeMap";

    /// <summary>Gets what is baked, such as <c>ToolObjects/LightProbeVolume</c>.</summary>
    private string? ToolObject => GetArgument(3);

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var isLightProbeVolume = LightProbeVolume.Equals(ToolObject, StringComparison.OrdinalIgnoreCase);

        if (!isLightProbeVolume && !CubeMap.Equals(ToolObject, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scene = Owner.Scene;
        var keyValues = Owner.KeyValues;

        SceneNode? node;

        if (isLightProbeVolume)
        {
            // Only a combined volume, which bakes a cubemap too, fades out over its edges
            var fadesAtEdges = Owner.Classname.Equals("env_combined_light_probe_volume", StringComparison.OrdinalIgnoreCase);

            node = SceneLightingVolume.AddLightProbe(scene, keyValues, Owner.RigidTransform, Owner.LayerName, isSphere: false, fadesAtEdges);
        }
        else
        {
            // Only env_cubemap has a sphere of influence, the other classes have a box
            var isSphere = Owner.Classname.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase);

            node = SceneLightingVolume.AddEnvironmentMap(scene, keyValues, Owner.RigidTransform, Owner.LayerName, isSphere);
        }

        // The nodes are part of the entity
        if (node != null)
        {
            node.EntityData = Owner.KeyValues;
        }
    }
}
