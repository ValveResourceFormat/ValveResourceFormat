using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// The environment map and light probe volume a scene node is lit with. A node only has one when it has
    /// cached lighting, either from handshakes of the map compiler or from the scene picking volumes for it.
    /// </summary>
    public sealed class SceneNodeLightingCache
    {
        /// <summary>
        /// Gets the handshakes of the environment map and light probe volume the map compiler assigned to the node.
        /// </summary>
        public PrecomputedLightingHandshakes Handshakes { get; init; }

        /// <summary>
        /// Gets the light probe volume the node is lit with, or <see langword="null"/> when it has none.
        /// </summary>
        public SceneLightProbe? LightProbe { get; internal set; }

        /// <summary>
        /// Gets the shader index of the one environment map the node reflects, or <see cref="SceneEnvMap.NoEnvMapIndex"/>
        /// when it reflects none because it names one the world does not contain.
        /// </summary>
        public uint EnvMapIndex { get; internal set; }

        /// <summary>
        /// Where the lighting of a moving node was last picked from, null when it has to be picked again.
        /// </summary>
        internal Vector3? CachedLightingOrigin { get; set; }

        /// <summary>Creates the lighting cache of a node that names precomputed volumes, or null when it names none.</summary>
        internal static SceneNodeLightingCache? FromHandshakes(PrecomputedLightingHandshakes handshakes)
            => handshakes.HasAny ? new SceneNodeLightingCache { Handshakes = handshakes } : null;

        // Returns whether the environment map has a shader index to be reflected from
        internal bool SetEnvMap(SceneEnvMap envMap)
        {
            if (envMap.ShaderIndex < 0)
            {
                return false;
            }

            EnvMapIndex = (uint)envMap.ShaderIndex;
            return true;
        }
    }
}
