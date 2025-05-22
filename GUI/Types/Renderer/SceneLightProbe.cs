using System.Buffers;
using System.Runtime.InteropServices;
using GUI.Types.Renderer.UniformBuffers;
using OpenTK.Graphics.OpenGL;

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

    private int bufferHandle = -1;

    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
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
                Matrix4x4.Invert(Transform, out var worldToLocal);

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
