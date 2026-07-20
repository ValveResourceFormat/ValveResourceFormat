namespace ValveResourceFormat.Renderer.Input;

/// <summary>
/// Closed-form, framerate-independent solvers for ground movement under combined friction
/// and acceleration: the walk-taper band and the no-prestrafe speed cap.
/// </summary>
public partial class PlayerMovement
{
    private const float WalkTaperBand = 5f;    // Walk acceleration tapers off over this many u/s below its zero-point
    private const float SpeedCapSlack = 0.5f;   // Float drift absorbed when comparing speeds against caps

    /// <summary>
    /// Exact walking acceleration. The taper's zero-point sits 5k/a above the goal so
    /// that the taper/friction balance A·(taperGoal - p)/5 = k·p lands exactly on the
    /// goal speed: inside the band the wishdir component follows another scalar
    /// exponential, settling at the goal at rate k + A/5. The frame splits at the band
    /// entry (or at the goal, coming down from above, where the addspeed gate keeps
    /// acceleration off); the perpendicular component is plain friction, which has
    /// already been applied.
    /// </summary>
    private static Vector3 WalkBandAccelerate(Vector3 velocity, Vector3 wishdir, float goalSpeed, Vector3 preFrictionVelocity, float deltaTime, float frictionRate, float accelMagnitude)
    {
        var taperGoal = goalSpeed + WalkTaperBand * frictionRate * goalSpeed / accelMagnitude;
        var bandStart = taperGoal - WalkTaperBand;
        var along = Vector3.Dot(preFrictionVelocity, wishdir);
        var time = deltaTime;

        if (along > goalSpeed)
        {
            // The addspeed gate keeps acceleration off above the goal: pure friction down to it
            var decayTime = MathF.Log(along / goalSpeed) / frictionRate;

            if (decayTime >= time)
            {
                along *= MathF.Exp(-frictionRate * time);
                time = 0f;
            }
            else
            {
                along = goalSpeed;
                time -= decayTime;
            }
        }
        else if (along < bandStart)
        {
            // Constant acceleration below the band
            var equilibrium = accelMagnitude / frictionRate;
            var entryTime = MathF.Log((equilibrium - along) / (equilibrium - bandStart)) / frictionRate;

            if (entryTime >= time)
            {
                along = equilibrium + (along - equilibrium) * MathF.Exp(-frictionRate * time);
                time = 0f;
            }
            else
            {
                along = bandStart;
                time -= entryTime;
            }
        }

        var bandRate = frictionRate + accelMagnitude / WalkTaperBand;
        along = goalSpeed + (along - goalSpeed) * MathF.Exp(-bandRate * time);

        return velocity + (along - Vector3.Dot(velocity, wishdir)) * wishdir;
    }

    /// <summary>
    /// No-prestrafe speed cap in closed form, framerate-independent. Under combined
    /// friction and acceleration the velocity follows dv/dt = -k·v + A·wishdir, which
    /// makes speed² a quadratic in u = e^(-kt); its smaller root is the exact moment the
    /// speed crosses the cap. From then on the trajectory rides the cap: the radial part
    /// of the acceleration is spent against the clamp while the tangential part rotates
    /// the velocity toward wishdir as dφ/dt = -(A/C)·sin φ, i.e. tan(φ/2) decays
    /// exponentially. Evaluating both phases analytically lands every framerate on the
    /// same end-of-frame velocity. Derivation: desmos.com/calculator/e93108decf
    /// </summary>
    private static Vector3 CapSpeedNoPrestrafe(Vector3 velocity, Vector3 wishdir, float wishspeed, float previousSpeed, Vector3 preFrictionVelocity, float deltaTime, float frictionRate, float accelMagnitude)
    {
        var cap = MathF.Max(wishspeed, previousSpeed);
        var startSpeed = preFrictionVelocity.Length();

        // The solves assume exponential-regime friction; crawl speeds keep the plain rescale
        if (wishspeed <= 0f || startSpeed <= StopSpeedValue)
        {
            return RescaleToCap(velocity, cap);
        }

        // A frame starting above wishspeed rides the friction decay envelope instead
        if (startSpeed > wishspeed + SpeedCapSlack)
        {
            return TryCapOverspeed(wishdir, wishspeed, preFrictionVelocity, startSpeed, cap, deltaTime, frictionRate, accelMagnitude, out var capped)
                ? capped
                : RescaleToCap(velocity, cap);
        }

        return RideCap(preFrictionVelocity, velocity, wishdir, cap, deltaTime, frictionRate, accelMagnitude);
    }

    /// <summary>
    /// Solves a frame that starts at or below the cap: |v(t)|² is a quadratic in
    /// u = e^(-kt), whose smaller root is the exact moment the speed reaches the cap
    /// (at a start exactly on the cap, that root self-selects between pinning and
    /// falling off, u = min(1, l/j)). From the crossing on, the tangential part of the
    /// acceleration rotates the velocity toward wishdir as dφ/dt = -(A/C)·sin φ, i.e.
    /// tan(φ/2) decays exponentially. Without a crossing, <paramref name="fallback"/> is
    /// kept, rescaled to the cap as a drift net.
    /// </summary>
    private static Vector3 RideCap(Vector3 vStart, Vector3 fallback, Vector3 wishdir, float cap, float time, float frictionRate, float accelMagnitude)
    {
        Vector3 Capped(Vector3 velocity)
        {
            var speed = velocity.Length();
            return speed > cap ? velocity * (cap / speed) : velocity;
        }

        var equilibrium = wishdir * (accelMagnitude / frictionRate);

        // |v(t)|² = j·u² + o·u + |equilibrium|², with u = e^(-kt) falling from 1
        var offset = vStart - equilibrium;
        var j = offset.LengthSquared();
        var o = 2f * Vector3.Dot(offset, equilibrium);
        var l = equilibrium.LengthSquared() - cap * cap;

        var discriminant = o * o - 4f * j * l;

        // Starting on the equilibrium, or on a trajectory that never reaches the cap
        if (j < 1e-6f || discriminant <= 0f)
        {
            return Capped(fallback);
        }

        var uCross = MathF.Min((-o - MathF.Sqrt(discriminant)) / (2f * j), 1f);

        if (uCross <= MathF.Exp(-frictionRate * time))
        {
            // The cap is not reached within this frame
            return Capped(fallback);
        }

        // Time left after the crossing at t = -ln(u)/k
        var rideTime = time + MathF.Log(uCross) / frictionRate;

        // Velocity at the crossing and its signed angle from wishdir
        var atCross = equilibrium + offset * uCross;
        var angle = MathF.Atan2(wishdir.X * atCross.Y - wishdir.Y * atCross.X, Vector3.Dot(wishdir, atCross));

        // Ride the cap for the rest of the frame; the crossing arrives with
        // cos φ ≥ k·C/A, so the angle is well inside (-π/2, π/2)
        angle = 2f * MathF.Atan(MathF.Tan(angle / 2f) * MathF.Exp(-accelMagnitude / cap * rideTime));

        var (sin, cos) = MathF.SinCos(angle);
        return cap * (cos * wishdir + sin * new Vector3(-wishdir.Y, wishdir.X, 0f));
    }

    /// <summary>
    /// Closed-form no-prestrafe frame starting above wishspeed: the speed rides the
    /// friction decay envelope max(wishspeed, s0·e^(-kt)) while acceleration only turns
    /// the velocity toward wishdir. Four phases, each analytic: no acceleration while the
    /// wishdir speed component exceeds wishspeed (angle frozen, Source's addspeed gate);
    /// a sliding phase holding that component at wishspeed (cos φ tracks w/R) while
    /// acceleration can keep up with friction (sin²φ ≥ k·w/A); envelope rotation
    /// dφ/dt = -(A/R(t))·sin φ; and the flat-cap rotation once the envelope sinks to
    /// wishspeed. Returns false for the paths this model does not cover (retreating from
    /// wishdir, or a wide-angle approach entering the sliding phase from below), which
    /// keep the plain rescale.
    /// </summary>
    private static bool TryCapOverspeed(Vector3 wishdir, float wishspeed, Vector3 preFrictionVelocity, float startSpeed, float cap, float deltaTime, float frictionRate, float accelMagnitude, out Vector3 result)
    {
        result = default;

        var along = Vector3.Dot(preFrictionVelocity, wishdir);
        var across = wishdir.X * preFrictionVelocity.Y - wishdir.Y * preFrictionVelocity.X;
        var absAngle = MathF.Atan2(MathF.Abs(across), along);

        var frictionAccelRatio = frictionRate * wishspeed / accelMagnitude;
        var sinSq = MathF.Sin(absAngle) * MathF.Sin(absAngle);

        if (along <= 0f || (along < wishspeed && sinSq > frictionAccelRatio))
        {
            return false;
        }

        // When the envelope meets wishspeed and the cap goes flat
        var envelopeEnd = MathF.Log(startSpeed / wishspeed) / frictionRate;

        // Frozen phase: the addspeed gate keeps acceleration off until R·cos φ = wishspeed
        var time = along > wishspeed ? MathF.Min(MathF.Log(along / wishspeed) / frictionRate, deltaTime) : 0f;

        // Sliding phase: acceleration saturates restoring the wishdir component,
        // pinning cos φ = wishspeed / R(t) until sin²φ falls to k·w/A
        if (time < deltaTime && time < envelopeEnd && sinSq > frictionAccelRatio)
        {
            var exitCos = MathF.Sqrt(1f - frictionAccelRatio);
            var slideEnd = MathF.Min(MathF.Min(MathF.Log(startSpeed * exitCos / wishspeed) / frictionRate, envelopeEnd), deltaTime);
            absAngle = MathF.Acos(Math.Clamp(wishspeed / (startSpeed * MathF.Exp(-frictionRate * slideEnd)), -1f, 1f));
            time = slideEnd;
        }

        // Envelope rotation: dφ/dt = -(A/R(t))·sin φ with R decaying, so tan(φ/2)
        // scales by exp(-(A/(k·s0))·(e^(k·t2) - e^(k·t1)))
        if (time < deltaTime && time < envelopeEnd)
        {
            var phaseEnd = MathF.Min(envelopeEnd, deltaTime);
            var factor = MathF.Exp(-accelMagnitude / (frictionRate * startSpeed) * (MathF.Exp(frictionRate * phaseEnd) - MathF.Exp(frictionRate * time)));
            absAngle = 2f * MathF.Atan(MathF.Tan(absAngle / 2f) * factor);
            time = phaseEnd;
        }

        // Past the envelope the pin only holds if acceleration outpaces friction
        // radially (cos φ ≥ k·w/A); otherwise the speed falls below wishspeed and the
        // free flight plus recrossing solve takes over for the rest of the frame
        if (time < deltaTime && MathF.Cos(absAngle) < frictionAccelRatio)
        {
            var fallAngle = across < 0f ? -absAngle : absAngle;
            var (fallSin, fallCos) = MathF.SinCos(fallAngle);
            var atCap = wishspeed * (fallCos * wishdir + fallSin * new Vector3(-wishdir.Y, wishdir.X, 0f));

            var equilibrium = wishdir * (accelMagnitude / frictionRate);
            var freeEnd = equilibrium + (atCap - equilibrium) * MathF.Exp(-frictionRate * (deltaTime - time));

            result = RideCap(atCap, freeEnd, wishdir, wishspeed, deltaTime - time, frictionRate, accelMagnitude);
            return true;
        }

        // Flat cap at wishspeed for the rest of the frame
        if (time < deltaTime)
        {
            absAngle = 2f * MathF.Atan(MathF.Tan(absAngle / 2f) * MathF.Exp(-accelMagnitude / wishspeed * (deltaTime - time)));
        }

        var angle = across < 0f ? -absAngle : absAngle;
        var (sin, cos) = MathF.SinCos(angle);
        result = cap * (cos * wishdir + sin * new Vector3(-wishdir.Y, wishdir.X, 0f));
        return true;
    }

    /// <summary>
    /// Rescales the velocity down to the cap when it exceeds it.
    /// </summary>
    private static Vector3 RescaleToCap(Vector3 velocity, float cap)
    {
        var speed = velocity.Length();
        return speed > cap ? velocity * (cap / speed) : velocity;
    }
}
