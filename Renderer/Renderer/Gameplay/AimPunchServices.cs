namespace ValveResourceFormat.Renderer.Gameplay;

/// <summary>Weapon recoil.</summary>
/// <remarks>
/// A shot does not move the punch angle directly. It pushes a velocity that decays exponentially, and the
/// angle integrates that velocity while decaying back to zero both exponentially and linearly. The curve is
/// stepped 128 times a second from the last shot, cached, and interpolated for whatever time is asked, so
/// the punch is smooth at any frame rate.
/// </remarks>
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

    private const float TickInterval = 1f / 64f;
    private const float StepsPerSecond = 128f;
    private const int MaxSteps = 128;
    private const float VelocityDecay = 4.5f;
    private const float LinearDecayPerStep = 18f / StepsPerSecond;
    private const float MinMagnitude = 1f / 32f;
    private const float MaxAngle = 89f;

    private static readonly float ExponentialDecayPerStep = MathF.Exp(-8f / StepsPerSecond);

    private float baseTime;
    private Vector2 baseAngle;
    private Vector2 baseVelocity;

    // Punch angle at each step after baseTime, filled in as far as a sample has needed.
    private readonly List<Vector2> steps = new(MaxSteps + 1);

    /// <summary>
    /// Kicks the punch for one shot.
    /// </summary>
    /// <param name="time">When the shot was fired, in seconds.</param>
    /// <param name="angle">Recoil angle from the weapon's recoil pattern, in degrees; 0 kicks straight up, positive to the right.</param>
    /// <param name="magnitude">Recoil magnitude from the weapon's recoil pattern.</param>
    /// <returns>The view punch the shot adds, as (pitch, yaw) degrees.</returns>
    public Vector2 Kick(float time, float angle, float magnitude)
    {
        // The old punch is read a tick ahead, but the curve restarts half a tick ahead
        var punch = Sample(time + TickInterval);
        var restartTime = time + TickInterval * 0.5f;
        var velocity = VelocityAt(restartTime - baseTime);

        var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
        var kick = new Vector2(cos, sin) * magnitude;

        baseTime = restartTime;
        baseAngle = punch;
        baseVelocity = velocity - kick;
        steps.Clear();

        return -kick * ViewPunchScale;
    }

    /// <summary>
    /// Gets the punch at a time, as (pitch, yaw) degrees. Negative pitch is up, as with Source angles.
    /// Scale by <see cref="ViewScale"/>, <see cref="ViewmodelScale"/> or <see cref="BulletScale"/> for use.
    /// </summary>
    /// <param name="time">Time in seconds, on the same clock as <see cref="Kick"/>.</param>
    public Vector2 Sample(float time)
    {
        var elapsed = time - baseTime;
        var position = elapsed > 0f ? elapsed * StepsPerSecond : 0f;
        var step = (int)position;

        if (steps.Count == 0)
        {
            steps.Add(baseAngle);
        }

        var lastStep = Math.Min(step + 1, MaxSteps);

        while (steps.Count <= lastStep)
        {
            var i = steps.Count;
            var punch = steps[i - 1] * ExponentialDecayPerStep;
            var length = punch.Length();

            punch = length > LinearDecayPerStep ? punch * (1f - LinearDecayPerStep / length) : Vector2.Zero;

            // Trapezoid rule over the step
            punch += (VelocityAt((i - 1) / StepsPerSecond) + VelocityAt(i / StepsPerSecond)) * (0.5f / StepsPerSecond);

            steps.Add(punch);
        }

        // A lerp stands in for a quaternion slerp; at punch sized angles they are the same
        var from = Vector2.Clamp(StepAt(step), new(-MaxAngle), new(MaxAngle));
        var to = Vector2.Clamp(StepAt(step + 1), new(-MaxAngle), new(MaxAngle));
        var result = Vector2.Lerp(from, to, position - step);

        return result.Length() < MinMagnitude ? Vector2.Zero : result;
    }

    /// <summary>
    /// Clears the punch.
    /// </summary>
    public void Reset()
    {
        baseTime = 0f;
        baseAngle = Vector2.Zero;
        baseVelocity = Vector2.Zero;
        steps.Clear();
    }

    private Vector2 VelocityAt(float elapsed)
    {
        var velocity = baseVelocity * MathF.Exp(-VelocityDecay * MathF.Max(elapsed, 0f));

        return velocity.Length() < MinMagnitude ? Vector2.Zero : velocity;
    }

    // Past the cached second only the exponential decay carries on
    private Vector2 StepAt(int step)
        => step < steps.Count
            ? steps[step]
            : steps[^1] * MathF.Pow(ExponentialDecayPerStep, step - steps.Count + 1);
}
