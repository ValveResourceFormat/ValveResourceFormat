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
    /// Displacement over a frame of pure friction: exponential decay above stopspeed
    /// (d = v·(1-e^(-kt))/k), constant deceleration below it, split at the crossing.
    /// </summary>
    private static Vector3 FrictionDisplacement(Vector3 velocity, float deltaTime, float frictionRate)
    {
        var speed = velocity.Length();

        if (speed < 0.1f)
        {
            return velocity * deltaTime;
        }

        var direction = velocity / speed;
        float distance;

        if (speed <= StopSpeedValue)
        {
            distance = LinearFrictionDistance(speed, deltaTime, frictionRate);
        }
        else
        {
            var timeToStopSpeed = MathF.Log(speed / StopSpeedValue) / frictionRate;

            if (deltaTime <= timeToStopSpeed)
            {
                distance = speed * (1f - MathF.Exp(-frictionRate * deltaTime)) / frictionRate;
            }
            else
            {
                distance = (speed - StopSpeedValue) / frictionRate
                    + LinearFrictionDistance(StopSpeedValue, deltaTime - timeToStopSpeed, frictionRate);
            }
        }

        return direction * distance;
    }

    /// <summary>
    /// Distance covered under the constant stopspeed·k deceleration, halting at zero.
    /// </summary>
    private static float LinearFrictionDistance(float speed, float time, float frictionRate)
    {
        var deceleration = StopSpeedValue * frictionRate;
        time = MathF.Min(time, speed / deceleration);
        return speed * time - deceleration * time * time / 2f;
    }

    /// <summary>
    /// Exact displacement of the friction+acceleration ODE dv/dt = -k·v + A·wishdir:
    /// d = E·t + (v0 - E)·(1-e^(-kt))/k with E the equilibrium velocity A/k·wishdir.
    /// </summary>
    private static Vector3 LinearOdeDisplacement(Vector3 velocity, Vector3 equilibrium, float deltaTime, float frictionRate)
    {
        return equilibrium * deltaTime + (velocity - equilibrium) * ((1f - MathF.Exp(-frictionRate * deltaTime)) / frictionRate);
    }

    /// <summary>
    /// Frame starting below stopspeed under acceleration: constant net acceleration
    /// A·wishdir - stopspeed·k·v̂ (linear-regime friction) until the speed crosses
    /// stopspeed, then the exponential ODE for the rest of the frame.
    /// </summary>
    private static (Vector3 Velocity, Vector3 Displacement) SubStopSpeedAccelerate(Vector3 v0, Vector3 wishdir, float accelMagnitude, float deltaTime, float frictionRate)
    {
        var frictionDirection = v0.LengthSquared() > 0.01f ? Vector3.Normalize(v0) : wishdir;
        var acceleration = accelMagnitude * wishdir - StopSpeedValue * frictionRate * frictionDirection;

        // Crossing time: |v0 + a·t| = stopspeed. Starting inside the stopspeed circle
        // there is exactly one positive root
        var tCross = deltaTime;
        var a2 = acceleration.LengthSquared();

        if (a2 > 1e-6f)
        {
            var b = 2f * Vector3.Dot(v0, acceleration);
            var c = v0.LengthSquared() - StopSpeedValue * StopSpeedValue;
            var discriminant = b * b - 4f * a2 * c;

            if (discriminant >= 0f)
            {
                var root = (-b + MathF.Sqrt(discriminant)) / (2f * a2);

                if (root >= 0f)
                {
                    tCross = MathF.Min(root, deltaTime);
                }
            }
        }

        var velocity = v0 + acceleration * tCross;
        var displacement = v0 * tCross + acceleration * (tCross * tCross / 2f);

        var remaining = deltaTime - tCross;

        if (remaining > 0f)
        {
            var equilibrium = wishdir * (accelMagnitude / frictionRate);
            displacement += LinearOdeDisplacement(velocity, equilibrium, remaining, frictionRate);
            velocity = equilibrium + (velocity - equilibrium) * MathF.Exp(-frictionRate * remaining);
        }

        return (velocity, displacement);
    }

    /// <summary>
    /// Second-order fallback displacement for trajectories without an implemented closed
    /// form; exact whenever velocity is linear in time over the frame.
    /// </summary>
    private static Vector3 TrapezoidDisplacement(Vector3 startVelocity, Vector3 endVelocity, float deltaTime)
    {
        return (startVelocity + endVelocity) * (0.5f * deltaTime);
    }

    /// <summary>
    /// Closed-form "tickless" air strafe. In the continuous limit the addspeed gate acts
    /// as a constraint: while view rotation pulls the wishdir ahead of the velocity, the
    /// wishdir component stays pinned at the air cap and the perpendicular component
    /// grows at exactly cap units per radian turned — so strafe gain depends on total
    /// rotation, not framerate. The frame solves in three phases: an instantaneous top-up
    /// of the wishdir component to the cap (key changes), a frozen phase while the
    /// component still exceeds the cap (gate closed, velocity unchanged), and the pinned
    /// rotation phase. Returns null when the acceleration budget cannot cover the frame
    /// (very high fps or low air accelerate), where the caller's discrete update is
    /// already linear in dt and framerate-independent.
    /// </summary>
    private static Vector3? TicklessAirStrafe(Vector3 velocity, Vector3 wishdirEnd, float cap, float yawDelta, float budget)
    {
        // Wishdir at the start of the frame's rotation
        var (sinBack, cosBack) = MathF.SinCos(-yawDelta);
        var wishdirStart = new Vector3(
            wishdirEnd.X * cosBack - wishdirEnd.Y * sinBack,
            wishdirEnd.X * sinBack + wishdirEnd.Y * cosBack,
            0f);

        var horizontal = new Vector3(velocity.X, velocity.Y, 0f);

        // Instantaneous top-up of the wishdir component to the cap
        var along = Vector3.Dot(horizontal, wishdirStart);
        var topUp = MathF.Max(0f, cap - along);
        var spent = topUp;
        horizontal += topUp * wishdirStart;

        var turnSign = MathF.Sign(yawDelta);
        var turn = MathF.Abs(yawDelta);

        if (turnSign != 0 && turn > 1e-6f)
        {
            var speed = horizontal.Length();
            along = Vector3.Dot(horizontal, wishdirStart);

            // Perpendicular component, normalized so positive means the rotation is
            // moving the wishdir toward the velocity
            var perp = turnSign * (wishdirStart.X * horizontal.Y - wishdirStart.Y * horizontal.X);

            var phi0 = MathF.Atan2(perp, along);
            var phiCap = MathF.Acos(Math.Clamp(cap / speed, -1f, 1f));

            // Gate closed while the wishdir component exceeds the cap: the wishdir swings
            // past the velocity and pinning starts once it leads by phiCap
            var pinStart = MathF.Max(0f, phi0 + phiCap);

            if (turn > pinStart)
            {
                var pinnedTurn = turn - pinStart;
                var perpAtPin = MathF.Sqrt(MathF.Max(0f, speed * speed - cap * cap));
                var perpMagnitude = perpAtPin + cap * pinnedTurn;

                // Acceleration spent pinning: ∫p dθ
                spent += perpAtPin * pinnedTurn + cap * pinnedTurn * pinnedTurn / 2f;

                // Reconstruct in end-of-frame axes; the velocity trails the rotation
                var perpDir = new Vector3(-wishdirEnd.Y, wishdirEnd.X, 0f);
                horizontal = cap * wishdirEnd - turnSign * perpMagnitude * perpDir;
            }
        }

        if (spent > budget)
        {
            return null;
        }

        return new Vector3(horizontal.X, horizontal.Y, velocity.Z);
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
