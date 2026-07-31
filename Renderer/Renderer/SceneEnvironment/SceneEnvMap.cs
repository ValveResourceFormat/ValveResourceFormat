using System.Numerics;
using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Environment map reflection probe with box or sphere projection.
/// </summary>
public class SceneEnvMap : SceneNode
{
    /// <summary>
    /// Skin added to the probe's box on every side. The shader tests fragments against the grown box, so
    /// anything deciding where the probe reaches must grow it by the same amount.
    /// </summary>
    public const float BoundsExtend = 0.02f;

    /// <summary>Gets the handshake value used to match this env map to scene nodes during precomputation.</summary>
    public int HandShake { get; init; }

    /// <summary>Gets the cubemap or cubemap-array texture for this environment map.</summary>
    public required RenderTexture EnvMapTexture { get; init; }

    /// <summary>Gets the color tint applied to reflections from this env map.</summary>
    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>
    /// If <see cref="EnvMapTexture"/> is an array, this is the depth index.
    /// </summary>
    public int ArrayIndex { get; init; }

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
    public int ShaderIndex { get; set; }

    /// <summary>
    /// Reduces one probe's baked radiance to the harmonic <see cref="NormalizationSH"/> holds.
    /// The texture stores L2 in RGB, planar, 27 floats per probe; only the constant and linear
    /// bands survive, weighted to luminance and folded together with the basis and cosine lobe
    /// constants so the shader can evaluate it as a plain dot with (normal, 1).
    /// </summary>
    /// <param name="radianceCoefficients">Coefficients for every probe in the array, or null.</param>
    /// <param name="arrayIndex">Index of this probe within the array.</param>
    public static Vector4 CalculateNormalizationSH(float[]? radianceCoefficients, int arrayIndex)
    {
        const int CoefficientsPerChannel = 9;
        const int CoefficientsPerProbe = CoefficientsPerChannel * 3;

        const float ConstantBand = 0.282095f;   // Y00
        const float LinearBand = 0.325735f;     // Y1x * 2/3

        var offset = arrayIndex * CoefficientsPerProbe;

        if (radianceCoefficients == null || arrayIndex < 0 || offset + CoefficientsPerProbe > radianceCoefficients.Length)
        {
            return NoNormalization;
        }

        // The same weights the shaders use for luma.
        var luminanceWeights = new Vector3(0.2125f, 0.7154f, 0.0721f);
        Span<float> luma = stackalloc float[4];

        for (var band = 0; band < luma.Length; band++)
        {
            var rgb = new Vector3(
                radianceCoefficients[offset + band],
                radianceCoefficients[offset + CoefficientsPerChannel + band],
                radianceCoefficients[offset + (CoefficientsPerChannel * 2) + band]);

            luma[band] = Vector3.Dot(rgb, luminanceWeights);
        }

        // Source 2 orders the linear band y, z, x, while the shader dots against (normal, 1).
        // Fitting a free 3x3 between a hand projection and the stored coefficients comes back
        // diagonal, so there is no permutation hiding in here beyond this reorder.
        return new Vector4(
            luma[3] * LinearBand,
            luma[1] * LinearBand,
            luma[2] * LinearBand,
            luma[0] * ConstantBand);
    }

    /// <summary>
    /// 128-bit bitmask tracking which environment maps are visible to a node.
    /// </summary>
    [InlineArray(4)]
    public struct EnvMapVisibility128
    {
        private uint _bucket0;

#pragma warning disable CA1024 // Use properties where appropriate
        /// <summary>Returns an enumeration of shader indices for all env maps currently marked visible in this bitmask.</summary>
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
        /// priority probe the node sees, which is what the legacy per batch cube map path binds. Ties
        /// resolve arbitrarily.
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
