using System.Buffers;
using System.Runtime.InteropServices;
using GUI.Types.Renderer.Buffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer;

#nullable disable

class SceneLightProbe : SceneNode
{
    public int HandShake { get; set; }

    /// <remarks>
    /// Used in lighting version 6 and 8.x
    /// </remarks>
    public RenderTexture Irradiance { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture DirectLightIndices { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture DirectLightScalars { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public RenderTexture DirectLightShadows { get; set; }

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

    private int bufferHandle = -1;


    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    public SceneAggregate DebugGridSpheres { get; private set; }

    public void CrateDebugGridSpheres()
    {
        if (DebugGridSpheres != null)
        {
            DebugGridSpheres.LayerEnabled = true;
            return;
        }

        DebugGridSpheres = new SceneAggregate(Scene, (Model)GLMaterialViewer.CubemapResource.Value.DataBlock)
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
            Log.Warn(nameof(CrateDebugGridSpheres), $"LightProbe {Id} has too many voxels ({numVoxels}) to visualize. Clamping to {MaxVoxels}.");
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

    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }

    public void SetGpuProbeData(bool isProbeAtlas)
    {
#if false // for debugging
        if (bufferHandle > -1)
        {
            GL.DeleteBuffer(bufferHandle);
            bufferHandle = -1;
        }
#endif

        // Note: does not expect the data to change within the probe's lifetime
        if (bufferHandle == -1)
        {
            GL.CreateBuffers(1, out bufferHandle);

            var size = 16 + (isProbeAtlas ? 16 : 8);
            var data = ArrayPool<float>.Shared.Rent(size);

            try
            {
                if (!Matrix4x4.Invert(Transform, out var worldToLocal))
                {
                    throw new InvalidOperationException("Matrix invert failed");
                }

                var normalizedMatrix = worldToLocal *
                    Matrix4x4.CreateTranslation(-LocalBoundingBox.Min) *
                    Matrix4x4.CreateScale(Vector3.One / LocalBoundingBox.Size);

                MemoryMarshal.Write(MemoryMarshal.Cast<float, byte>(data.AsSpan()[0..16]), in normalizedMatrix);

                if (isProbeAtlas)
                {
                    var half = Vector3.One * 0.5f;
                    var depthDivide = Vector3.One with { Z = 1f / 6 };

                    var textureDims = new Vector3(DirectLightShadows.Width, DirectLightShadows.Height, DirectLightShadows.Depth);
                    var atlasDims = AtlasOffset + AtlasSize;

                    var borderMin = half * depthDivide / textureDims;
                    var borderMax = (textureDims - half) * depthDivide / textureDims;
                    var scale = AtlasSize / textureDims;
                    var offset = AtlasOffset / textureDims;

                    var vectors = MemoryMarshal.Cast<float, Vector4>(data.AsSpan()[16..32]);
                    vectors[0] = new Vector4(borderMin, 0);
                    vectors[1] = new Vector4(borderMax, 0);
                    vectors[2] = new Vector4(scale, 0);
                    vectors[3] = new Vector4(offset, 0);
                }
                else
                {
                    var vectors = MemoryMarshal.Cast<float, Vector4>(data.AsSpan()[16..24]);
                    vectors[0] = Vector4.Zero; // Layer0TextureMin
                    vectors[1] = Vector4.Zero; // Layer0TextureMax
                }

                GL.NamedBufferData(bufferHandle, size * sizeof(float), data, BufferUsageHint.StaticDraw);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(data);
            }
        }

        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, (int)ReservedBufferSlots.LightProbe, bufferHandle);
    }
}
