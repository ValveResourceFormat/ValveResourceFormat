namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pulls screen space particles toward the bottom of the local player's view, the way world gravity
/// falls across the screen as the view rolls and pitches: X is pushed by <c>m_flAccel</c> times the
/// height of the view's left axis, and Y against the height of its up axis. Without a player there
/// is no force.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/particles/C_OP_ScreenForceFromPlayerView">C_OP_ScreenForceFromPlayerView</seealso>
class ScreenForceFromPlayerView : ParticleFunctionForceGenerator
{
    private readonly float accel;

    public ScreenForceFromPlayerView(ParticleDefinitionParser parse) : base(parse)
    {
        accel = parse.Float("m_flAccel", accel);
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        if (particleSystemState.Player is not { } player)
        {
            return;
        }

        var orientation = player.GetOrientation(eyes: true);
        var left = Vector3.Transform(Vector3.UnitY, orientation);
        var up = Vector3.Transform(Vector3.UnitZ, orientation);
        var force = new Vector3(accel * left.Z, -accel * up.Z, 0f);

        for (var i = 0; i < forces.Length; i++)
        {
            forces[i] += force;
        }
    }
}
