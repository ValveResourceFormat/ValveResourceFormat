using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Scene node for indirect lighting via light probe volumes.
/// </summary>
public class SceneLightProbe : SceneNode
{
    public int HandShake { get; set; }

    /// <remarks>
    /// Used in lighting version 6 and 8.x
    /// </remarks>
    public RenderTexture? Irradiance { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture? DirectLightIndices { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture? DirectLightScalars { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public RenderTexture? DirectLightShadows { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public Vector3 AtlasSize { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public Vector3 AtlasOffset { get; set; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    public float VoxelSize { get; set; }

    public int ShaderIndex { get; set; }


    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    public SceneAggregate? DebugGridSpheres { get; private set; }

    public void CrateDebugGridSpheres()
    {
        if (DebugGridSpheres != null)
        {
            DebugGridSpheres.LayerEnabled = true;
            return;
        }

        var cubemapModel = ShapeSceneNode.CubemapResource.Value.DataBlock as Model;
        if (cubemapModel == null)
        {
            throw new InvalidOperationException("CubemapResource DataBlock is not a Model");
        }

        DebugGridSpheres = new SceneAggregate(Scene, cubemapModel)
        {
            LightProbeVolumePrecomputedHandshake = LightProbeVolumePrecomputedHandshake,
            LightProbeBinding = this,
            LayerName = "LightProbeGrid" + Id,
            LayerEnabled = true,
            HasTransforms = true,
        };

        DebugGridSpheres.SetInfiniteBoundingBox();
        Scene.Add(DebugGridSpheres, true);

        const int MaxVoxels = 100_000;

        var grid = LocalBoundingBox.Size / (VoxelSize + 0.5f);
        var numVoxels = (int)(grid.X * grid.Y * grid.Z);

        if (numVoxels > MaxVoxels)
        {
            Scene.RendererContext.Logger.LogWarning("LightProbe {ProbeId} has too many voxels ({NumVoxels}) to visualize. Clamping to {MaxVoxels}", Id, numVoxels, MaxVoxels);
            numVoxels = MaxVoxels;
        }

        DebugGridSpheres.InstanceTransforms.EnsureCapacity(numVoxels);

        for (var x = 0; x < grid.X; x++)
        {
            for (var y = 0; y < grid.Y; y++)
            {
                for (var z = 0; z < grid.Z; z++)
                {
                    var localPosition = LocalBoundingBox.Min + new Vector3(x, y, z) * VoxelSize + new Vector3(VoxelSize / 2f);
                    var worldPosition = Vector3.Transform(localPosition, Transform);
                    var transform = Matrix4x4.CreateScale(0.2f * (VoxelSize / 24f)) * Matrix4x4.CreateTranslation(worldPosition);

                    DebugGridSpheres.InstanceTransforms.Add(transform.To3x4());

                    if (DebugGridSpheres.InstanceTransforms.Count >= MaxVoxels)
                    {
                        break;
                    }
                }
            }
        }
    }

    public void RemoveDebugGridSpheres()
    {
        if (DebugGridSpheres != null)
        {
            DebugGridSpheres.LayerEnabled = false;
        }
    }

    public LightProbeVolume CalculateGpuProbeData(bool isProbeAtlas)
    {
        if (!Matrix4x4.Invert(Transform, out var worldToLocal))
        {
            throw new InvalidOperationException("Matrix invert failed");
        }

        var data = new LightProbeVolume
        {
            WorldToLocalVolumeNormalized = worldToLocal *
                Matrix4x4.CreateTranslation(-LocalBoundingBox.Min) *
                Matrix4x4.CreateScale(Vector3.One / LocalBoundingBox.Size)
        };

        if (isProbeAtlas)
        {
            if (DirectLightShadows == null)
            {
                throw new InvalidOperationException("DirectLightShadows is null but probe atlas is expected");
            }

            var half = Vector3.One * 0.5f;
            var depthDivide = Vector3.One with { Z = 1f / 6 };

            var textureDims = new Vector3(DirectLightShadows.Width, DirectLightShadows.Height, DirectLightShadows.Depth);
            var atlasDims = AtlasOffset + AtlasSize;

            var borderMin = half * depthDivide / textureDims;
            var borderMax = (textureDims - half) * depthDivide / textureDims;
            var scale = AtlasSize / textureDims;
            var offset = AtlasOffset / textureDims;

            data.BorderMin = new Vector4(borderMin, 0);
            data.BorderMax = new Vector4(borderMax, 0);
            data.AtlasScale = new Vector4(scale, 0);
            data.AtlasOffset = new Vector4(offset, 0);
        }

        return data;
    }
}
