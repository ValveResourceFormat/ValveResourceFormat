namespace ValveResourceFormat.Renderer.Input;

// A Deadlock hero does not step up stairs the way a CS player does. Its body floats a step height above the
// ground on a spring, so stairs, lips and the steep risers between ramps pass under it, and its feet follow
// whatever floor a probe below the body finds. The hull here stays a box: the part above the step height is
// the body that collides, and the part below is the reach of the spring.
public partial class PlayerMovement
{
    private const float DlPogoFrequency = 10f;           // sv_mover_pogofrequency, critically damped (sv_mover_pogodampingratio 1)
    private const float DlGroundQuadScale = 0.5f;        // how far out the quad's corners sit, as a share of the hull

    private float DlPogoSpeed;
    private float DlGroundZ;

    // The part of the hull above the step height, which is what collides while walking
    private Vector3 DeadlockBodyHalfExtents(Vector3 halfExtents)
        => halfExtents with { Z = halfExtents.Z - (StepHeight * 0.5f) };

    private Vector3 DeadlockBodyCenter(Vector3 position) => position + new Vector3(0f, 0f, StepHeight * 0.5f);

    /// <summary>
    /// Finds the floor under the body: rays from the bottom of the body to <paramref name="reach"/> below the
    /// feet, at the centre and the corners of a quad around it. The floor is the mean height of the rays that
    /// land on walkable ground, so a stair edge or a steep riser under one of them does not decide it, and
    /// stairs read as the slope through their edges.
    /// </summary>
    private bool TryFindDeadlockGround(Vector3 position, Vector3 halfExtents, float reach, out float groundZ)
        => TryFindDeadlockGround(position, halfExtents, reach, out groundZ, out _);

    /// <param name="position">The hull centre.</param>
    /// <param name="halfExtents">The hull half extents.</param>
    /// <param name="reach">How far below the feet floor is looked for.</param>
    /// <param name="groundZ">The height of the floor found.</param>
    /// <param name="steepNormal">With no walkable floor found, the normal of a slope too steep to stand on that
    /// was found instead, or zero.</param>
    private bool TryFindDeadlockGround(Vector3 position, Vector3 halfExtents, float reach, out float groundZ, out Vector3 steepNormal)
    {
        steepNormal = Vector3.Zero;
        var feet = position.Z - halfExtents.Z;
        var corner = new Vector2(halfExtents.X, halfExtents.Y) * DlGroundQuadScale;

        Span<Vector2> offsets = [Vector2.Zero, corner, -corner, new(corner.X, -corner.Y), new(-corner.X, corner.Y)];

        var sum = 0f;
        var hits = 0;

        foreach (var offset in offsets)
        {
            var probeStart = new Vector3(position.X + offset.X, position.Y + offset.Y, feet + StepHeight);
            var trace = TraceBBoxRaw(probeStart, probeStart with { Z = feet - reach }, Vector3.Zero, detectStartSolid: false);

            if (trace.Hit && trace.HitNormal.Z >= WalkableSlope)
            {
                sum += trace.HitPosition.Z;
                hits++;
            }
            else if (trace.Hit && trace.HitNormal.Z > 0f)
            {
                steepNormal = trace.HitNormal;
            }
        }

        groundZ = hits > 0 ? sum / hits : 0f;

        if (hits > 0)
        {
            steepNormal = Vector3.Zero;
        }

        return hits > 0;
    }

    /// <summary>
    /// Deadlock's ground check: on the ground when a floor lies within a step below the feet, which the spring
    /// then reaches.
    /// </summary>
    private void DeadlockCategorizePosition(Vector3 position, Vector3 halfExtents)
    {
        // Rising off the ground leaves it. In the air only a fall is caught, or the spring would cut jumps
        // short, unless the hull rests on walkable ground already, as one pushed up a stair edge does
        var catchSpeed = OnGround || IsWalkableGroundHit(TraceBBox(position, position - new Vector3(0f, 0f, GroundProbeDistance), halfExtents))
            ? NonJumpVelocity
            : 0f;

        if (Velocity.Z > catchSpeed)
        {
            OnGround = false;
            return;
        }

        OnGround = TryFindDeadlockGround(position, halfExtents, StepHeight, out var groundZ);
        GroundEntity = null;
        SurfaceFriction = 1f;

        if (OnGround)
        {
            DlGroundZ = groundZ;
        }
        else
        {
            DlPogoSpeed = 0f;
        }
    }

    /// <summary>
    /// One frame of walking (or gliding) on the ground: the body slides along whatever it meets above the
    /// step height, and the feet spring to the floor found under its new place.
    /// </summary>
    private Vector3 DeadlockGroundMove(Vector3 position, Vector3 wishdir, float wishspeed, float deltaTime, Vector3 halfExtents)
    {
        const int MaxBumps = 4;

        if (IsSliding || IsDashing)
        {
            Velocity += DeadlockGlideDelta(deltaTime, position, halfExtents);
        }
        else
        {
            WalkMove(wishdir, wishspeed, deltaTime, DuckSpeedModifierActive, isWalking: false);
        }

        Velocity = Horizontal(Velocity);

        var bodyHalfExtents = DeadlockBodyHalfExtents(halfExtents);
        var body = DeadlockBodyCenter(position);
        var delta = Velocity * deltaTime;

        for (var bump = 0; bump < MaxBumps && delta.LengthSquared() > UntraceableDistanceSquared; bump++)
        {
            var trace = TraceBBox(body, body + delta, bodyHalfExtents);

            if (!trace.Hit)
            {
                body += delta;
                break;
            }

            NoteImpact(trace);

            var travelled = trace.HitPosition - body;
            body = trace.HitPosition;

            // Whatever the body meets is a wall to it: anything walkable lies under the step height
            var wall = Horizontal(trace.HitNormal);

            if (wall.LengthSquared() < 1e-6f)
            {
                break;
            }

            wall = Vector3.Normalize(wall);
            delta -= travelled;
            delta -= wall * MathF.Min(0f, Vector3.Dot(delta, wall));
            Velocity -= wall * MathF.Min(0f, Vector3.Dot(Velocity, wall));
        }

        var start = position;
        position = body - new Vector3(0f, 0f, StepHeight * 0.5f);

        if (!TryFindDeadlockGround(position, halfExtents, StepHeight, out var groundZ, out var steepNormal))
        {
            // Walked onto the foot of a slope too steep to stand on: it is a wall like any other. Moving
            // away from one, or with nothing under the feet at all, walked off an edge and the fall
            // starts from here.
            var slope = Horizontal(steepNormal);

            if (slope.LengthSquared() < 1e-6f || Vector3.Dot(position - start, slope) >= 0f)
            {
                OnGround = false;
                return DeadlockLeaveGround(position, halfExtents);
            }

            slope = Vector3.Normalize(slope);
            Velocity -= slope * MathF.Min(0f, Vector3.Dot(Velocity, slope));
            position = start;
            groundZ = DlGroundZ;
        }

        DlGroundZ = groundZ;

        var feet = position.Z - halfExtents.Z;
        var offset = MathUtils.CriticallyDampedSpring(feet - groundZ, ref DlPogoSpeed, MathF.Tau * DlPogoFrequency, deltaTime);
        var risen = position with { Z = groundZ + offset + halfExtents.Z };

        // The spring may not lift the body into a ceiling
        if (risen.Z > position.Z)
        {
            var bodyFrom = DeadlockBodyCenter(position);
            var ceiling = TraceBBox(bodyFrom, DeadlockBodyCenter(risen), bodyHalfExtents);

            if (ceiling.Hit)
            {
                risen.Z = ceiling.HitPosition.Z - (StepHeight * 0.5f);
                DlPogoSpeed = 0f;
            }
        }

        return risen;
    }

    // Walking, the feet may sit in the steps while the spring catches up. Leaving the ground lowers the
    // whole hull from the bottom of the body onto whatever is under it, so the air move starts clear.
    // Under a low ceiling the hull is only lifted as far as the body fits.
    private Vector3 DeadlockLeaveGround(Vector3 position, Vector3 halfExtents)
    {
        DlPogoSpeed = 0f;

        var body = DeadlockBodyCenter(position);
        var bodyAbove = body + new Vector3(0f, 0f, StepHeight);
        var lift = TraceBBox(body, bodyAbove, DeadlockBodyHalfExtents(halfExtents));
        var above = position + ((lift.Hit ? lift.HitPosition : bodyAbove) - body);
        var settle = TraceBBox(above, position, halfExtents);

        return settle.Hit && !settle.StartSolid ? settle.HitPosition : position;
    }
}
