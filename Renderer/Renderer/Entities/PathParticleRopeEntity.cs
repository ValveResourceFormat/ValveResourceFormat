using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>path_particle_rope</c>, <c>path_particle_rope_clientside</c> and Deadlock's <c>citadel_zipline_path</c>:
/// a cable hung along the entity's path nodes, which follows the entity.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPathParticleRope">CPathParticleRope</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/server/CCitadelZiplinePath">CCitadelZiplinePath</seealso>
public sealed class PathParticleRopeEntity : BaseEntity
{
    /// <summary>Initializes a rope from its keyvalues.</summary>
    public PathParticleRopeEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // Points per spline segment of a zipline's ride path
    private const int ZiplineSegmentSamples = 16;

    private Vector3[]? ziplinePath;

    /// <summary>Gets whether this is a Deadlock zipline, which a player can ride.</summary>
    public bool IsZipline => Classname == "citadel_zipline_path";

    /// <summary>
    /// Gets the line a zipline is ridden along, in world space, following its spline without the cable's
    /// droop. Empty for a rope that is not a zipline or has fewer than two path nodes.
    /// </summary>
    public IReadOnlyList<Vector3> ZiplinePath => ziplinePath ??= BuildZiplinePath();

    private Vector3[] BuildZiplinePath()
    {
        var nodes = PathParticleRope.ParseNodes(KeyValues.GetStringProperty("pathnodes"));

        if (!IsZipline || nodes.Count < 2)
        {
            return [];
        }

        var transform = EntityTransformHelper.ToRigidTransformationMatrix(WorldAngles, WorldOrigin);
        var path = new Vector3[((nodes.Count - 1) * ZiplineSegmentSamples) + 1];

        for (var i = 0; i < nodes.Count - 1; i++)
        {
            var start = nodes[i].Position;
            var end = nodes[i + 1].Position;
            var startHandle = start + nodes[i].OutTangent;
            var endHandle = end + nodes[i + 1].InTangent;

            for (var j = 0; j < ZiplineSegmentSamples; j++)
            {
                var t = j / (float)ZiplineSegmentSamples;
                var u = 1f - t;
                var point = (u * u * u * start) + (3f * u * u * t * startHandle) + (3f * u * t * t * endHandle) + (t * t * t * end);
                path[(i * ZiplineSegmentSamples) + j] = Vector3.Transform(point, transform);
            }
        }

        path[^1] = Vector3.Transform(nodes[^1].Position, transform);
        return path;
    }

    /// <summary>Builds the cable. A rope that has none draws nothing, not even an editor marker.</summary>
    /// <returns>The cable, or <see langword="null"/>.</returns>
    protected override SceneNode? CreateRootNode()
    {
        try
        {
            if (!CableSceneNode.TryCreate(Scene, KeyValues, out var cable))
            {
                EntitySystem.Logger.LogWarning("Skipped degenerate {Classname} '{Target}' at ({Origin})", Classname, TargetName, WorldOrigin);
                return null;
            }

            cable.LayerName = Scene.ParticlesLayerName;

            return cable;
        }
        catch (Exception e)
        {
            EntitySystem.Logger.LogError(e, "Failed to setup {Classname} '{Target}'", Classname, TargetName);
            return null;
        }
    }
}
