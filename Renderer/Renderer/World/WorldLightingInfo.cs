namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Scene lighting data including lightmaps, reflection probes, and shadow maps.
    /// </summary>
    // VKTODO: Port the rest: lightmaps, light probes, env maps, barn lights and sun shadows
    public class WorldLightingInfo
    {
        /// <summary>Gets or sets the GPU lighting constants buffer for the scene.</summary>
        public LightingConstants LightingData { get; set; } = new();
    }
}
