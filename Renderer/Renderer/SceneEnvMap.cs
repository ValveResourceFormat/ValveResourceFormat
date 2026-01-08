using System.Runtime.CompilerServices;

namespace GUI.Types.Renderer;

public class SceneEnvMap : SceneNode
{
    public int HandShake { get; init; }
    public required RenderTexture EnvMapTexture { get; init; }

    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>
    /// If <see cref="EnvMapTexture"/> is an array, this is the depth index.
    /// </summary>
    public int ArrayIndex { get; init; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    public Vector3 EdgeFadeDists { get; init; }

    /// <summary>
    /// 0 = Sphere, 1 = Box
    /// </summary>
    public int ProjectionMode { get; init; }

    public int ShaderIndex { get; set; }

    [InlineArray(4)]
    public struct EnvMapVisibility128
    {
        private uint _bucket0;

#pragma warning disable CA1024 // Use properties where appropriate
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

        public readonly (uint, uint, uint, uint) ToTuple() => (this[0], this[1], this[2], this[3]);
    }

    public SceneEnvMap(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }
}
