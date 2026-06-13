namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Provides information about which particle function classes are supported by the renderer.
/// </summary>
public static class ParticleSupportInfo
{
    /// <summary>
    /// Checks whether the given Source 2 class name is a supported emitter.
    /// </summary>
    public static bool IsEmitterSupported(string name) => ParticleControllerFactory.EmitterDictionary.ContainsKey(name);

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported initializer.
    /// </summary>
    public static bool IsInitializerSupported(string name) => ParticleControllerFactory.InitializerDictionary.ContainsKey(name);

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported operator.
    /// </summary>
    public static bool IsOperatorSupported(string name) => ParticleControllerFactory.OperatorDictionary.ContainsKey(name);

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported force generator.
    /// </summary>
    public static bool IsForceGeneratorSupported(string name) => ParticleControllerFactory.ForceGeneratorDictionary.ContainsKey(name);

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported renderer.
    /// </summary>
    public static bool IsRendererSupported(string name) => ParticleControllerFactory.RendererDictionary.ContainsKey(name);

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported pre-emission operator.
    /// </summary>
    public static bool IsPreEmissionOperatorSupported(string name) => ParticleControllerFactory.PreEmissionOperatorDictionary.ContainsKey(name);
}
