using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node for indirect lighting via light probe volumes.
/// </summary>
public class SceneLightProbe : SceneNode
{
    /// <summary>Gets or sets the handshake value used to match this probe to scene nodes during precomputation.</summary>
    public int HandShake { get; set; }

    /// <summary>Gets or sets the irradiance probe texture (lighting version 6 and 8.x).</summary>
    public RenderTexture? Irradiance { get; set; }

    /// <summary>Gets or sets the direct light index texture (lighting version 8.1).</summary>
    public RenderTexture? DirectLightIndices { get; set; }

    /// <summary>Gets or sets the direct light scalar texture (lighting version 8.1).</summary>
    public RenderTexture? DirectLightScalars { get; set; }

    /// <summary>Gets or sets the direct light shadow texture (lighting version 8.2).</summary>
    public RenderTexture? DirectLightShadows { get; set; }

    /// <summary>Gets or sets the probe atlas size in texels (lighting version 8.2).</summary>
    public Vector3 AtlasSize { get; set; }

    /// <summary>Gets or sets the probe atlas offset in texels (lighting version 8.2).</summary>
    public Vector3 AtlasOffset { get; set; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    /// <summary>Gets or sets the world-space size of each voxel cell in the probe grid.</summary>
    public float VoxelSize { get; set; }

    /// <summary>Gets or sets the shader-side index assigned to this probe for UBO packing.</summary>
    public int ShaderIndex { get; set; }


    /// <summary>
    /// Initializes a new instance of the <see cref="SceneLightProbe"/> class with the given local-space bounds.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="bounds">The local-space bounds of the light probe volume.</param>
    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    /// <summary>Gets the aggregate scene node used to visualize the probe grid spheres, if created.</summary>
    public SceneAggregate? DebugGridSpheres { get; private set; }

    /// <summary>
    /// Creates or re-enables the debug grid sphere visualization for this light probe volume.
    /// </summary>
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

    /// <summary>Hides the debug grid sphere visualization without destroying it.</summary>
    public void RemoveDebugGridSpheres()
    {
        if (DebugGridSpheres != null)
        {
            DebugGridSpheres.LayerEnabled = false;
        }
    }

    /// <summary>
    /// Computes the <see cref="LightProbeVolume"/> GPU data for this probe, including atlas offsets when applicable.
    /// </summary>
    /// <param name="isProbeAtlas">Whether the probe uses a probe atlas texture (lighting version 8.2).</param>
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
