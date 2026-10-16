namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node for indirect lighting via light probe volumes.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvLightProbeVolume">CEnvLightProbeVolume</seealso>
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

    /// <summary>
    /// Gets or sets the size of the volume's probe grid in the textures it shares with other volumes, in texels.
    /// The depth counts one of the six ambient cube faces the irradiance stacks. Zero when the textures are its own.
    /// </summary>
    public Vector3 AtlasSize { get; set; }

    /// <summary>Gets or sets where the volume's probe grid starts in the textures it shares with other volumes, in texels.</summary>
    public Vector3 AtlasOffset { get; set; }

    /// <summary>
    /// Gets or sets the size of the irradiance texture the volume was baked into, when it has since been copied
    /// into a larger one. Sampling keeps half a texel inside of it.
    /// </summary>
    public Vector3? BakedTextureSize { get; set; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    /// <summary>Gets the distance in from each side of the box over which the volume fades out.</summary>
    public Vector3 EdgeFadeDists { get; init; }

    /// <summary>Gets or sets the world-space size of each voxel cell in the probe grid.</summary>
    public float VoxelSize { get; set; }

    /// <summary>Gets or sets the shader-side index assigned to this probe for UBO packing.</summary>
    public int ShaderIndex { get; set; }

    internal Matrix4x4 WorldToLocal = Matrix4x4.Identity;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneLightProbe"/> class with the given local-space bounds.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="bounds">The local-space bounds of the light probe volume.</param>
    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    /// <summary>
    /// Computes the <see cref="LightProbeVolume"/> GPU data for this probe. The irradiance texture stacks the six
    /// ambient cube faces in depth, so sampling is kept half a texel inside of the first face, and the volume's
    /// place in an atlas is only applied when its grid is smaller than the texture.
    /// </summary>
    public LightProbeVolume CalculateGpuProbeData()
    {
        if (!Matrix4x4.Invert(Transform, out var worldToLocal))
        {
            throw new InvalidOperationException("Matrix invert failed");
        }

        WorldToLocal = worldToLocal;

        var data = new LightProbeVolume
        {
            WorldToLocalVolumeNormalized = worldToLocal *
                Matrix4x4.CreateTranslation(-LocalBoundingBox.Min) *
                Matrix4x4.CreateScale(Vector3.One / LocalBoundingBox.Size),

            InvEdgeWidth = new Vector4(LocalBoundingBox.Size / Vector3.Max(EdgeFadeDists, new Vector3(SceneEnvMap.BoundsExtend)), 0),
            AtlasScale = Vector4.One,
        };

        if (Irradiance == null)
        {
            return data;
        }

        var textureSize = new Vector3(Irradiance.Width, Irradiance.Height, Irradiance.Depth);
        var halfTexel = new Vector3(0.5f) / (BakedTextureSize ?? textureSize);

        data.BorderMin = new Vector4(halfTexel, 0);
        data.BorderMax = new Vector4(1f - halfTexel.X, 1f - halfTexel.Y, (1f / 6f) - halfTexel.Z, 0);

        // The depth of the grid counts one face, of the six the texture stacks
        var stackedSize = AtlasSize with { Z = AtlasSize.Z * 6f };

        if (AtlasSize is { X: > 0, Y: > 0, Z: > 0 } && (stackedSize.X < textureSize.X || stackedSize.Y < textureSize.Y || stackedSize.Z < textureSize.Z))
        {
            data.AtlasScale = new Vector4(stackedSize / textureSize, 0);
            data.AtlasOffset = new Vector4(AtlasOffset with { Z = AtlasOffset.Z * 6f } / textureSize, 0);
        }

        return data;
    }
}
