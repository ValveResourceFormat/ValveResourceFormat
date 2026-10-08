namespace ValveResourceFormat.Particles;

/// <summary>How much a <see cref="ParticleDiagnostic"/> matters to what the effect shows.</summary>
public enum ParticleDiagnosticSeverity
{
    /// <summary>Worth knowing, but the effect looks as authored.</summary>
    Info,

    /// <summary>Part of the effect may look different from the game, or be missing.</summary>
    Warning,

    /// <summary>The system cannot show anything as things stand.</summary>
    Error,
}

/// <summary>
/// Something about a particle system that keeps the preview from matching the game: an unimplemented
/// function, an input the game would supply but the preview has not, or a system that is not
/// producing particles.
/// </summary>
/// <param name="Severity">How much it matters.</param>
/// <param name="System">The file of the system it concerns.</param>
/// <param name="Component">The function, renderer or input concerned.</param>
/// <param name="Problem">What is wrong.</param>
/// <param name="Impact">What it does to the rendered effect.</param>
/// <param name="Resolution">What would fix it, or what input is missing.</param>
public sealed record ParticleDiagnostic(
    ParticleDiagnosticSeverity Severity,
    string System,
    string Component,
    string Problem,
    string Impact,
    string Resolution)
{
    /// <summary>Gets where the system sits in the tree, from the root down to it.</summary>
    public string Path { get; init; } = System;
}

/// <summary>Counts over a particle system and every system below it.</summary>
/// <param name="Systems">Systems in the tree, the root included.</param>
/// <param name="ActiveSystems">Systems currently running, rather than waiting, disabled or filtered out.</param>
/// <param name="LiveParticles">Particles alive across the tree.</param>
/// <param name="EmittedParticles">Particles emitted across the tree since the last replay.</param>
/// <param name="SystemsWithParticles">Systems holding at least one live particle.</param>
public readonly record struct ParticleSystemStatistics(int Systems, int ActiveSystems, int LiveParticles, long EmittedParticles, int SystemsWithParticles);
