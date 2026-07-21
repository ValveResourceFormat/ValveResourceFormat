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
    /// Frame starting below stopspeed under acceleration. The constant-magnitude friction
    /// there opposes the (rotating) velocity direction, so with a misaligned wishdir the
    /// ODE has no elementary closed form; instead the continuous model — thrust gated at
    /// wishspeed along wishdir, friction -stopspeed·k·v̂ below stopspeed and -k·v above —
    /// is integrated with fixed-size RK4 substeps. The substep is small enough that the
    /// result tracks the continuous trajectory (and therefore composes across any frame
    /// partitioning) far below float noise.
    /// </summary>
    private static (Vector3 Velocity, Vector3 Displacement) SubStopSpeedAccelerate(Vector3 v0, Vector3 wishdir, float wishspeed, float accelMagnitude, float deltaTime, float frictionRate)
    {
        const float MaxSubstep = 1f / 2048f;

        var steps = Math.Max(1, (int)MathF.Ceiling(deltaTime / MaxSubstep));
        var h = deltaTime / steps;
        var v = v0;
        var displacement = Vector3.Zero;

        for (var i = 0; i < steps; i++)
        {
            var k1 = SubStopSpeedAcceleration(v, wishdir, wishspeed, accelMagnitude, frictionRate);
            var v2 = v + h / 2f * k1;
            var k2 = SubStopSpeedAcceleration(v2, wishdir, wishspeed, accelMagnitude, frictionRate);
            var v3 = v + h / 2f * k2;
            var k3 = SubStopSpeedAcceleration(v3, wishdir, wishspeed, accelMagnitude, frictionRate);
            var v4 = v + h * k3;
            var k4 = SubStopSpeedAcceleration(v4, wishdir, wishspeed, accelMagnitude, frictionRate);

            // Simpson displacement over the same stages
            displacement += h / 6f * (v + 2f * v2 + 2f * v3 + v4);
            v += h / 6f * (k1 + 2f * k2 + 2f * k3 + k4);
        }

        return (v, displacement);
    }

    /// <summary>
    /// dv/dt of the continuous sub-stopspeed model: regime-aware friction plus wishdir
    /// thrust gated at wishspeed.
    /// </summary>
    private static Vector3 SubStopSpeedAcceleration(Vector3 velocity, Vector3 wishdir, float wishspeed, float accelMagnitude, float frictionRate)
    {
        var speed = velocity.Length();

        // At rest the friction direction is undefined; it opposes the impending
        // motion along wishdir, and cannot push backwards
        if (speed <= 1e-6f)
        {
            return MathF.Max(0f, accelMagnitude - StopSpeedValue * frictionRate) * wishdir;
        }

        var friction = speed > StopSpeedValue
            ? -frictionRate * velocity
            : -(StopSpeedValue * frictionRate / speed) * velocity;

        // The addspeed gate as a continuous constraint
        var thrust = Vector3.Dot(velocity, wishdir) < wishspeed ? accelMagnitude * wishdir : Vector3.Zero;
        return friction + thrust;
    }

    /// <summary>
    /// Minimum of |v(t)|² along the friction+acceleration ODE over the frame, where
    /// |v(u)|² is a quadratic in u = e^(-kt) falling from 1 to <paramref name="decayEnd"/>.
    /// Used to prove a frame never leaves the exponential friction regime.
    /// </summary>
    private static float OdeMinSpeedSquared(Vector3 v0, Vector3 equilibrium, float decayEnd)
    {
        var offset = v0 - equilibrium;
        var j = offset.LengthSquared();
        var o = 2f * Vector3.Dot(offset, equilibrium);
        var e2 = equilibrium.LengthSquared();

        var min = MathF.Min(j + o + e2, j * decayEnd * decayEnd + o * decayEnd + e2);

        if (j > 1e-12f)
        {
            var uStar = -o / (2f * j);

            if (uStar > decayEnd && uStar < 1f)
            {
                min = MathF.Min(min, e2 - o * o / (4f * j));
            }
        }

        return min;
    }

    /// <summary>
    /// Exact displacement of a gate-bound frame in the exponential regime: the ODE runs
    /// until the wishdir component reaches wishspeed, then the pinned phase holds it there
    /// while the perpendicular component keeps decaying under friction.
    /// </summary>
    private static Vector3 GateBoundDisplacement(Vector3 v0, Vector3 wishdir, float wishspeed, Vector3 equilibrium, float deltaTime, float frictionRate)
    {
        var along0 = Vector3.Dot(v0, wishdir);
        var equilibriumAlong = Vector3.Dot(equilibrium, wishdir);

        // Time the wishdir component crosses wishspeed: u* = e^(-k·t*)
        var pinTime = 0f;

        if (along0 < wishspeed)
        {
            var denominator = along0 - equilibriumAlong;

            if (MathF.Abs(denominator) < 1e-6f)
            {
                return TrapezoidDisplacement(v0, wishspeed * wishdir + (v0 - along0 * wishdir) * MathF.Exp(-frictionRate * deltaTime), deltaTime);
            }

            var uCross = (wishspeed - equilibriumAlong) / denominator;

            if (uCross <= 0f || uCross >= 1f)
            {
                return TrapezoidDisplacement(v0, wishspeed * wishdir + (v0 - along0 * wishdir) * MathF.Exp(-frictionRate * deltaTime), deltaTime);
            }

            pinTime = MathF.Min(MathF.Log(uCross) / -frictionRate, deltaTime);
        }

        var displacement = LinearOdeDisplacement(v0, equilibrium, pinTime, frictionRate);

        var pinned = deltaTime - pinTime;

        if (pinned > 0f)
        {
            var perp0 = v0 - along0 * wishdir;
            displacement += wishspeed * pinned * wishdir
                + perp0 * ((MathF.Exp(-frictionRate * pinTime) - MathF.Exp(-frictionRate * deltaTime)) / frictionRate);
        }

        return displacement;
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
    /// Normalizes an angle to +-pi.
    /// </summary>
    private static double NormalizeAngle(double angle)
    {
        // Reduce angle to [-2pi, 2pi]
        double normalized = angle % (2 * Math.PI);

        // Shift to [-pi, pi]
        if (normalized > Math.PI)
            normalized -= 2 * Math.PI;
        else if (normalized < -Math.PI)
            normalized += 2 * Math.PI;

        return normalized;
    }

    /// <summary>
    /// Tickless air strafe: the frame's uniform view rotation is integrated exactly by a
    /// three-regime state machine (per H7perus' CSGO-Tickless-Movement analysis), so
    /// strafe gain depends on degrees turned and the accel rate, not framerate.
    /// Regimes, in the rotation-normalized frame (g = wishdir velocity component,
    /// q = component ahead of the rotation):
    ///  - frozen: g above the cap, gate closed, velocity constant while the wishdir
    ///    swings until it leads the velocity by acos(cap/speed);
    ///  - pinned: g held at the cap; |q| grows at cap per radian, needing accel
    ///    cap-per-radian rate |q| — holds while that stays within the available rate;
    ///  - full accel: g below the cap (or the pin unaffordable); the constant accel
    ///    rate along the rotating wishdir integrates as a spiral, and g follows
    ///    R·sin(θ+φ), giving the re-pinning angle in closed form.
    /// Returns null for zero-rotation frames (the caller's discrete update is exact) or
    /// if the state machine fails to advance (degenerate tangency).
    /// </summary>
    private static Vector3? TicklessAirStrafe(Vector3 velocity, Vector3 wishdirEnd, float cap, float yawDelta, float accelRate, float deltaTime)
    {
        var turnSign = MathF.Sign(yawDelta);
        var turn = MathF.Abs(yawDelta);

        // Below this rotation the discrete update matches the continuous model to O(turn²)
        // anyway, and accelPerRadian (~1/turn) would amplify float noise pointlessly
        if (turnSign == 0 || turn <= 2e-4f)
        {
            return null;
        }

        // Accel available per radian of rotation
        var accelPerRadian = accelRate * deltaTime / turn;

        var endAngle = MathF.Atan2(wishdirEnd.Y, wishdirEnd.X);
        var startAngle = endAngle - yawDelta;

        var vx = velocity.X;
        var vy = velocity.Y;
        var theta = 0f;

        const int MaxPhases = 10;
        const float AngleEpsilon = 1e-7f;


        for (var phase = 0; phase < MaxPhases; phase++)
        {
            if (theta >= turn - AngleEpsilon)
            {
                return new Vector3(vx, vy, velocity.Z);
            }

            //TODO: Maybe rename this here, idk
            var stepEndAngle = (float)NormalizeAngle(startAngle + turnSign * theta);



            var sinR = MathF.Sin(stepEndAngle);
            var cosR = MathF.Sin(stepEndAngle > 0 ? MathF.PI / 2 - stepEndAngle : stepEndAngle + MathF.PI / 2);

            var g = vx * cosR + vy * sinR;
            var q = turnSign * (cosR * vy - sinR * vx);

            const float CapEpsilon = 1e-3f;

            if (g > cap + CapEpsilon || (g >= cap - CapEpsilon && q > 0f))
            {
                // Frozen: gate closed (component above the cap, or the rotation is moving
                // toward the velocity), velocity constant while the wishdir rotates until
                // it leads the velocity by acos(cap/speed)
                var speed = MathF.Sqrt(vx * vx + vy * vy);
                var phi = MathF.Atan2(q, g);
                var phiCap = MathF.Acos(Math.Clamp(cap / speed, -1f, 1f));
                var step = MathF.Min(turn - theta, phi + phiCap);

                if (step <= AngleEpsilon)
                {
                    return null;
                }

                theta += step;
                continue;
            }

            if (g >= cap - CapEpsilon && -q < accelPerRadian)
            {
                // Pinned: hold g at the cap until the required rate |q| reaches the
                // available rate or the frame ends

                //TODO: Second part of this min(a, b) has to be explained properly
                var step = MathF.Min(turn - theta, (accelPerRadian + q) / cap);

                theta += step;
                q -= cap * step;

                stepEndAngle = (float)NormalizeAngle(startAngle + turnSign * theta);

                sinR = MathF.Sin(stepEndAngle);
                cosR = MathF.Sin(stepEndAngle > 0 ? MathF.PI / 2 - stepEndAngle : stepEndAngle + MathF.PI / 2);

                vx = cap * cosR - q * turnSign * sinR;
                vy = cap * sinR + q * turnSign * cosR;
                continue;
            }

            // Below the cap, or at it with the pin unaffordable: full accel
            {
                // Full accel along the rotating wishdir: g(θ) = R·sin(θ+φ) with
                // g' = q + A', so the upward crossing of the cap is in closed form
                var gRate = q + accelPerRadian;
                var amplitude = MathF.Sqrt(g * g + gRate * gRate);
                float step;

                if (amplitude <= cap + 1e-4f)
                {
                    step = turn - theta;
                }
                else
                {
                    var phi = MathF.Atan2(g, gRate);
                    var hit = MathF.Asin(Math.Clamp(cap / amplitude, -1f, 1f)) - phi;

                    // Already at the crossing: the previous spiral step's landing error
                    // scales with the amplitude, so g can miss the pin window and re-enter
                    // here with the crossing solve reporting ~zero. Wrapping that by a full
                    // circle would integrate full accel through the gate for the rest of
                    // the frame; instead snap the wishdir component to the cap and let the
                    // dispatcher re-classify the regime
                    if (MathF.Abs(hit) <= 1e-5f)
                    {
                        vx += (cap - g) * cosR;
                        vy += (cap - g) * sinR;
                        continue;
                    }

                    // Genuinely past the crossing (falling off the pin): next crossing is
                    // a full period away
                    if (hit < 0f)
                    {
                        hit += MathF.Tau;
                    }

                    step = MathF.Min(turn - theta, hit);
                }

                if (step <= AngleEpsilon)
                {
                    return null;
                }

                // Spiral integral of the constant accel rate over the rotation
                theta += step;

                stepEndAngle = (float)NormalizeAngle(startAngle + turnSign * theta);

                var sinR2 = MathF.Sin(stepEndAngle);
                var cosR2 = MathF.Sin(stepEndAngle > 0 ? MathF.PI / 2 - stepEndAngle : stepEndAngle + MathF.PI / 2);

                vx += accelPerRadian * turnSign * (sinR2 - sinR);
                vy += accelPerRadian * turnSign * (cosR - cosR2);
            }
        }

        return null;
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
