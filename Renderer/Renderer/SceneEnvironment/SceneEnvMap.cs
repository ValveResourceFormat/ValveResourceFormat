using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Environment map reflection probe with box or sphere projection.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvCubemap">CEnvCubemap</seealso>
public class SceneEnvMap : SceneNode
{
    /// <summary>
    /// Skin added to the probe's box on every side. The shader tests fragments against the grown box, so
    /// anything deciding where the probe reaches must grow it by the same amount.
    /// </summary>
    public const float BoundsExtend = 0.1f;

    /// <summary>
    /// Reciprocal of an edge fade distance per axis, for the shader to scale distances from the edge by.
    /// No fade gives a hard edge rather than a division by zero.
    /// </summary>
    /// <param name="edgeFadeDists">The fade distance on each axis.</param>
    public static Vector3 GetInverseEdgeFade(Vector3 edgeFadeDists)
    {
        static float Inverse(float distance) => distance > 0f ? 1f / distance : float.MaxValue;

        return new Vector3(Inverse(edgeFadeDists.X), Inverse(edgeFadeDists.Y), Inverse(edgeFadeDists.Z));
    }

    /// <summary>
    /// Gets the volume of the probe's influence, which orders probes of the same priority, smaller first.
    /// </summary>
    public float InfluenceVolume
    {
        get
        {
            if (ProjectionMode == 0)
            {
                var radius = LocalBoundingBox.Max.X;
                return 4f / 3f * MathF.PI * radius * radius * radius;
            }

            var size = LocalBoundingBox.Size;
            return size.X * size.Y * size.Z;
        }
    }

    /// <summary>Gets the handshake value used to match this env map to scene nodes during precomputation.</summary>
    public int HandShake { get; init; }

    /// <summary>
    /// Gets or sets the cubemap or cubemap array texture for this environment map. A cubemap of its own is replaced by
    /// the array it is packed into.
    /// </summary>
    public required RenderTexture EnvMapTexture { get; set; }

    /// <summary>Gets or sets the index of the cube in <see cref="EnvMapTexture"/> when it is an array.</summary>
    public int ArrayIndex { get; set; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    /// <summary>Gets the per-axis edge fade distances used for box projection blending.</summary>
    public Vector3 EdgeFadeDists { get; init; }

    /// <summary>
    /// 0 = Sphere, 1 = Box
    /// </summary>
    public int ProjectionMode { get; init; }

    /// <summary>
    /// Gets the box in local space that materials with box projection project the reflection onto, or
    /// <see langword="null"/> when the entity names none and the bounds of the influence volume are used.
    /// </summary>
    public AABB? ProjectionBounds { get; init; }

    /// <summary>
    /// Luminance of this probe's own baked radiance, as an L1 spherical harmonic packed for
    /// a dot with (normal, 1). It covers the whole probe volume unoccluded, so dividing the
    /// per fragment irradiance by it gives specular occlusion.
    /// </summary>
    public Vector4 NormalizationSH { get; init; } = NoNormalization;

    /// <summary>
    /// Stands in for a probe whose coefficients could not be read. This evaluates to zero rather
    /// than to one: the harmonic holds a radiance, so no value in it means "leave reflections
    /// alone", and the shader skips the division when it comes out non positive instead.
    /// </summary>
    public static readonly Vector4 NoNormalization = Vector4.Zero;

    /// <summary>Gets or sets the shader-side index assigned to this env map for UBO packing.</summary>
    public int ShaderIndex { get; set; } = -1;

    internal Matrix4x4 WorldToLocal = Matrix4x4.Identity;

    /// <summary>
    /// Reduces one probe's baked radiance to the harmonic <see cref="NormalizationSH"/> holds.
    /// The texture stores L2 in RGB, planar, 27 floats per probe; only the constant and linear
    /// bands survive, weighted to luminance and folded together with the basis and cosine lobe
    /// constants so the shader can evaluate it as a plain dot with (normal, 1).
    /// </summary>
    /// <param name="radianceCoefficients">Coefficients for every probe in the array, or null.</param>
    /// <param name="arrayIndex">Index of this probe within the array.</param>
    /// <param name="transform">Placement of the env map; its rotation turns the linear band into world space.</param>
    public static Vector4 CalculateNormalizationSH(float[]? radianceCoefficients, int arrayIndex, in Matrix4x4 transform)
    {
        const int CoefficientsPerChannel = 9;
        const int CoefficientsPerProbe = CoefficientsPerChannel * 3;

        const float ConstantBand = 0.282095f;   // Y00
        const float LinearBand = 0.325735f;     // Y1x * 2/3
        const float MaxLinearToConstant = 0.99f;

        var offset = arrayIndex * CoefficientsPerProbe;

        if (radianceCoefficients == null || arrayIndex < 0 || offset + CoefficientsPerProbe > radianceCoefficients.Length)
        {
            return NoNormalization;
        }

        Span<float> luma = stackalloc float[4];

        for (var band = 0; band < luma.Length; band++)
        {
            var rgb = new Vector3(
                radianceCoefficients[offset + band],
                radianceCoefficients[offset + CoefficientsPerChannel + band],
                radianceCoefficients[offset + (CoefficientsPerChannel * 2) + band]);

            luma[band] = Vector3.Dot(rgb, MathUtils.Rec709LuminanceWeights);
        }

        var constant = luma[0] * ConstantBand;

        if (constant <= 0f)
        {
            return new Vector4(0f, 0f, 0f, 0.01f);
        }

        // The linear band is stored y, z, x, with y and x of opposite sign to the normal's axes
        var localLinear = new Vector3(-luma[3], -luma[1], luma[2]) * LinearBand;
        var linear = Vector3.TransformNormal(localLinear, transform);

        // Keeps dot((normal, 1), sh) positive for every normal; the length includes the constant term
        var length = new Vector4(linear, constant).Length();
        var scale = MathF.Min(constant * MaxLinearToConstant / length, 1f);

        return new Vector4(linear * scale, constant);
    }

    /// <summary>Shader index of a node that has no environment map.</summary>
    public const uint NoEnvMapIndex = 0xFFFF;

    /// <summary>
    /// 128-bit bitmask tracking which environment maps are visible to a node.
    /// </summary>
    [InlineArray(4)]
    public struct EnvMapVisibility128
    {
        private uint _bucket0;

        /// <summary>Every item visible.</summary>
        public static EnvMapVisibility128 Full => Unsafe.BitCast<UInt128, EnvMapVisibility128>(UInt128.MaxValue);

#pragma warning disable CA1024 // Use properties where appropriate
        /// <summary>Shader indices of every env map marked visible in this bitmask.</summary>
        public readonly IEnumerable<int> GetVisibleShaderIndices()
        {
            for (var bucket = 0; bucket < 4; bucket++)
            {
                for (var bitIndex = 0; bitIndex < 32; bitIndex++)
                {
                    if ((this[bucket] & (1u << bitIndex)) != 0)
                    {
                        yield return bucket * 32 + bitIndex;
                    }
                }
            }
        }
#pragma warning restore CA1024

        /// <summary>
        /// Sets the bits for each env map's <see cref="SceneEnvMap.ShaderIndex"/> in this bitmask and returns the result.
        /// </summary>
        public EnvMapVisibility128 Store(List<SceneEnvMap> envMaps)
        {
            foreach (var envMap in envMaps)
            {
                var index = envMap.ShaderIndex;

                if (index < 0)
                {
                    continue;
                }

                var bucketIndex = index / 32;

                // Only the first 128 probes get a bit here. The shader reads a probe past that as visible
                // to every object, leaving the tile and bin masks and the volume test to cull it, so the
                // gap costs a little iteration rather than a wrong result.
                if (bucketIndex >= 4)
                {
                    continue;
                }

                var bitIndex = index % 32;

                this[bucketIndex] |= 1u << bitIndex;
            }

            return this;
        }

        /// <summary>
        /// Returns the array index of the lowest env map set here, or zero when empty. Since
        /// <see cref="SceneEnvMap.ShaderIndex"/> is assigned in indoor priority order, that is the highest
        /// priority probe the node sees. Ties resolve arbitrarily.
        /// </summary>
        public readonly uint GetFirstShaderIndex()
        {
            for (var bucket = 0; bucket < 4; bucket++)
            {
                if (this[bucket] != 0u)
                {
                    return (uint)((bucket * 32) + BitOperations.TrailingZeroCount(this[bucket]));
                }
            }

            return 0u;
        }

        /// <summary>Returns the four 32-bit buckets of this bitmask as a value tuple.</summary>
        public readonly (uint, uint, uint, uint) ToTuple() => (this[0], this[1], this[2], this[3]);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneEnvMap"/> class with the given local-space bounds.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="bounds">The local-space bounds of the env map volume.</param>
    public SceneEnvMap(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }
}
