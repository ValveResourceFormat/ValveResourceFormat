namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// The environment map and light probe volume a map compiler assigned to a node, each as a handshake id that
    /// the lighting objects of the world are registered under. Zero names nothing.
    /// </summary>
    /// <param name="CubeMap">Handshake of the environment map.</param>
    /// <param name="LightProbeVolume">Handshake of the light probe volume.</param>
    public readonly record struct PrecomputedLightingHandshakes(int CubeMap, int LightProbeVolume)
    {
        /// <summary>Gets whether an environment map is named.</summary>
        public bool HasCubeMap => CubeMap > 0;

        /// <summary>Gets whether a light probe volume is named.</summary>
        public bool HasLightProbeVolume => LightProbeVolume > 0;

        /// <summary>Gets whether an environment map or a light probe volume is named.</summary>
        public bool HasAny => HasCubeMap || HasLightProbeVolume;
    }
}
