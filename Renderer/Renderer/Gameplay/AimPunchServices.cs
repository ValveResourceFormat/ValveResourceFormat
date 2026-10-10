namespace ValveResourceFormat.Renderer.Gameplay;

/// <summary>Weapon recoil.</summary>
/// <remarks>
/// A shot does not move the punch angle directly. It pushes a velocity that decays exponentially, and the
/// angle integrates that velocity while decaying back to zero both exponentially and linearly.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CCSPlayer_AimPunchServices">CCSPlayer_AimPunchServices</seealso>
public sealed class AimPunchServices
{
    /// <summary>Scale from the punch to where bullets go.</summary>
    public const float BulletScale = 2f;

    /// <summary>Scale from the punch to the rendered view: 0.45 of the bullet offset.</summary>
    public const float ViewScale = BulletScale * 0.45f;

    /// <summary>
    /// Scale from the punch to the first person weapon, which turns another 0.325 of the bullet offset
    /// past the view angles. Those already carry <see cref="ViewScale"/>.
    /// </summary>
    public const float ViewmodelScale = ViewScale + BulletScale * 0.325f;

    /// <summary>Share of each shot's kick that also goes into the view punch.</summary>
    public const float ViewPunchScale = 0.055f;

    private const float VelocityDecay = 4.5f;
    private const float ExponentialDecay = 8f;
    private const float LinearDecay = 18f;

    private Vector2 velocity;

    /// <summary>
    /// Gets the punch as (pitch, yaw) degrees. Negative pitch is up, as with Source angles.
    /// Scale by <see cref="ViewScale"/>, <see cref="ViewmodelScale"/> or <see cref="BulletScale"/> for use.
    /// </summary>
    public Vector2 Punch { get; private set; }

    /// <summary>
    /// Kicks the punch for one shot.
    /// </summary>
    /// <param name="angle">Recoil angle from the weapon's recoil pattern, in degrees; 0 kicks straight up, positive to the right.</param>
    /// <param name="magnitude">Recoil magnitude from the weapon's recoil pattern.</param>
    /// <returns>The view punch the shot adds, as (pitch, yaw) degrees.</returns>
    public Vector2 Kick(float angle, float magnitude)
    {
        var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
        var kick = new Vector2(cos, sin) * magnitude;

        velocity -= kick;

        return -kick * ViewPunchScale;
    }

    /// <summary>
    /// Advances the punch by a frame.
    /// </summary>
    /// <param name="deltaTime">Frame time in seconds.</param>
    public void Update(float deltaTime)
    {
        velocity *= MathF.Exp(-VelocityDecay * deltaTime);

        var punch = Punch * MathF.Exp(-ExponentialDecay * deltaTime);
        var length = punch.Length();
        var linear = LinearDecay * deltaTime;

        Punch = (length > linear ? punch * (1f - linear / length) : Vector2.Zero) + velocity * deltaTime;
    }
}
