namespace ValveResourceFormat.Particles;

/// <summary>
/// Reports which particle function classes the simulation implements. Renderer classes are
/// answered separately by whatever draws the system.
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
    /// Checks whether the given Source 2 class name is a supported constraint.
    /// </summary>
    public static bool IsConstraintSupported(string name) => ParticleControllerFactory.ConstraintDictionary.ContainsKey(name);


    /// <summary>
    /// Checks whether the given Source 2 class name is a supported pre-emission operator.
    /// </summary>
    public static bool IsPreEmissionOperatorSupported(string name) => ParticleControllerFactory.PreEmissionOperatorDictionary.ContainsKey(name);
}
