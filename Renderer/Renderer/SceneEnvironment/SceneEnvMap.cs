using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Environment map reflection probe with box or sphere projection.
/// </summary>
public class SceneEnvMap : SceneNode
{
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

    /// <summary>Gets or sets the shader-side index assigned to this env map for UBO packing.</summary>
    public int ShaderIndex { get; set; }

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
                var bitIndex = index % 32;

                this[bucketIndex] |= 1u << bitIndex;
            }

            return this;
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
