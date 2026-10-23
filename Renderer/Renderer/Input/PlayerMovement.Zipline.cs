using ValveResourceFormat.Renderer.Entities;

namespace ValveResourceFormat.Renderer.Input;

/// <summary>Where a Deadlock hero is with a zipline.</summary>
public enum DeadlockZiplineState
{
    /// <summary>Not on a zipline.</summary>
    None,

    /// <summary>Being pulled up to the line by the tether.</summary>
    Latching,

    /// <summary>Riding along the line.</summary>
    Riding,
}

// Deadlock ziplines: holding jump near one tethers the hero to it, and it rides the way the view points.
// Jump hops off with most of the speed and a lift, duck drops straight off.
public partial class PlayerMovement
{
    private const float DlZipMountDistance = 590.55f;    // MaxMountDistance2D, 15 m
    private const float DlZipHoldTime = 0.175f;          // m_flMinButtonHoldTimeToActivate
    private const float DlZipLatchInitialSpeed = 600f;
    private const float DlZipLatchSpeed = 1500f;
    private const float DlZipLatchMaxTime = 0.5f;
    private const float DlZipLatchEndSpeed = 750f;       // the ride starts this fast and settles to its own speed
    private const float DlZipSpeed = 693f;               // ZipSpeedInner
    private const float DlZipAcceleration = 1000f;
    private const float DlZipDismountSpeedScale = 0.85f; // DismountHorizontalMaxSpeedPercent
    private const float DlZipDropSpeedScale = 0.4f;      // DismountHorizontalMinSpeedPercent
    private const float DlZipDismountUpSpeed = 300f;
    private const float DlZipHangDepth = 80f;            // the feet hang this far under the line
    private const float DlZipLookTolerance = 80f;        // m_flRestrictedLookTolerance, degrees off the view

    private IReadOnlyList<Vector3> DlZipPath = [];
    private int DlZipSegment;
    private float DlZipSegmentFraction;
    private int DlZipDirection = 1;
    private float DlZipSpeedCurrent;
    private float DlZipLatchTime;
    private Vector3 DlZipLatchStart;
    private bool DlZipWaitForJumpRelease;
    private float DlZipHeld;

    /// <summary>Gets where the hero is with a zipline.</summary>
    public DeadlockZiplineState ZiplineState { get; private set; }

    private void ResetZipline()
    {
        ZiplineState = DeadlockZiplineState.None;
        DlZipPath = [];
    }

    /// <summary>
    /// Looks for a zipline within mounting distance of a hero that has held jump long enough, and starts the
    /// tether to the nearest point on it. A tap only jumps.
    /// </summary>
    private bool TryMountZipline(Vector3 position, Vector3 halfExtents, float deltaTime)
    {
        if (!Input.Holding(TrackedKeys.Space))
        {
            DlZipWaitForJumpRelease = false;
            DlZipHeld = 0f;
            return false;
        }

        // The jump that hopped off a line is still held, and must not latch straight back on
        if (DlZipWaitForJumpRelease || Input.EntitySystem is not { } entities)
        {
            return false;
        }

        DlZipHeld += deltaTime;

        if (DlZipHeld < DlZipHoldTime)
        {
            return false;
        }

        var feet = position - new Vector3(0f, 0f, halfExtents.Z);
        var bestDistance = DlZipMountDistance * DlZipMountDistance;
        IReadOnlyList<Vector3>? bestPath = null;
        var bestSegment = 0;
        var bestFraction = 0f;

        foreach (var entity in entities.Entities)
        {
            if (entity is not PathParticleRopeEntity { IsZipline: true } zipline || zipline.ZiplinePath is not { Count: > 1 } path)
            {
                continue;
            }

            for (var i = 0; i < path.Count - 1; i++)
            {
                var fraction = ClosestFraction(path[i], path[i + 1], feet);
                var point = Vector3.Lerp(path[i], path[i + 1], fraction);

                // The line has to hang over the hero, not lie below it, and be roughly where it looks
                if (point.Z < feet.Z || Vector3.Dot(Vector3.Normalize(point - EyePosition), Input.Camera.Forward) < MathF.Cos(float.DegreesToRadians(DlZipLookTolerance)))
                {
                    continue;
                }

                var distance = new Vector2(point.X - feet.X, point.Y - feet.Y).LengthSquared();

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPath = path;
                    bestSegment = i;
                    bestFraction = fraction;
                }
            }
        }

        if (bestPath == null)
        {
            return false;
        }

        DlZipPath = bestPath;
        DlZipSegment = bestSegment;
        DlZipSegmentFraction = bestFraction;

        // Ride the way the view points along the line
        var tangent = bestPath[bestSegment + 1] - bestPath[bestSegment];
        DlZipDirection = Vector3.Dot(tangent, Input.Camera.Forward) >= 0f ? 1 : -1;

        DlZipLatchStart = position;
        DlZipHeld = 0f;
        DlZipLatchTime = 0f;
        DlZipSpeedCurrent = MathF.Max(Horizontal(Velocity).Length(), 0f);
        DlZipWaitForJumpRelease = true;
        ZiplineState = DeadlockZiplineState.Latching;
        OnGround = false;
        DlDashTimer = -1f;
        DlSlideTimer = -1f;
        DlAirDashTimer = -1f;

        return true;
    }

    private static float ClosestFraction(Vector3 start, Vector3 end, Vector3 point)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();

        return lengthSquared > 0f ? MathUtils.Saturate(Vector3.Dot(point - start, segment) / lengthSquared) : 0f;
    }

    private Vector3 ZiplinePoint => Vector3.Lerp(DlZipPath[DlZipSegment], DlZipPath[DlZipSegment + 1], DlZipSegmentFraction);

    private Vector3 ZiplineTangent => Vector3.Normalize(DlZipPath[DlZipSegment + 1] - DlZipPath[DlZipSegment]) * DlZipDirection;

    /// <summary>
    /// One frame on a zipline: the tether pulling the hero up to the line, or the ride along it. The hero is
    /// carried, so nothing it would collide with stops it.
    /// </summary>
    private void DeadlockZiplineFrame(Camera camera, float deltaTime)
    {
        var halfExtents = HullHalfExtents;
        var hang = new Vector3(0f, 0f, DlZipHangDepth - halfExtents.Z);

        if (!Input.Holding(TrackedKeys.Space))
        {
            DlZipWaitForJumpRelease = false;
        }

        if (ZiplineState == DeadlockZiplineState.Latching)
        {
            // The tether speeds up as it pulls, and the hero arrives when it does or when the time is up
            DlZipLatchTime += deltaTime;

            var target = ZiplinePoint - hang;
            var toTarget = target - TracePosition;
            var distance = toTarget.Length();
            var speed = float.Lerp(DlZipLatchInitialSpeed, DlZipLatchSpeed, MathUtils.Saturate(DlZipLatchTime / DlZipLatchMaxTime));
            var step = speed * deltaTime;

            if (distance <= step || DlZipLatchTime >= DlZipLatchMaxTime)
            {
                TracePosition = target;
                ZiplineState = DeadlockZiplineState.Riding;
                DlZipSpeedCurrent = MathF.Max(DlZipSpeedCurrent, DlZipLatchEndSpeed);
            }
            else
            {
                TracePosition += toTarget / distance * step;
            }

            Velocity = distance > 0f ? toTarget / distance * speed : Vector3.Zero;
        }
        else
        {
            var speedChange = DlZipAcceleration * deltaTime;
            DlZipSpeedCurrent = DlZipSpeedCurrent > DlZipSpeed
                ? MathF.Max(DlZipSpeed, DlZipSpeedCurrent - speedChange)
                : MathF.Min(DlZipSpeed, DlZipSpeedCurrent + speedChange);

            if (!AdvanceAlongZipline(DlZipSpeedCurrent * deltaTime))
            {
                DismountZipline(DlZipDismountSpeedScale, DlZipDismountUpSpeed);
                return;
            }

            TracePosition = ZiplinePoint - hang;
            Velocity = ZiplineTangent * DlZipSpeedCurrent;

            if (Input.Pressed(TrackedKeys.Space) && !DlZipWaitForJumpRelease)
            {
                DismountZipline(DlZipDismountSpeedScale, DlZipDismountUpSpeed);
                NoteMove(DeadlockMove.ZiplineDismount, Horizontal(Velocity));
                return;
            }

            if (Input.Pressed(TrackedKeys.Control))
            {
                DismountZipline(DlZipDropSpeedScale, 0f);
                return;
            }
        }

        TracePositionSmooth = TracePosition;
        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = 0f;
    }

    // Moves along the line by the distance, false once it runs off the end
    private bool AdvanceAlongZipline(float distance)
    {
        while (distance > 0f)
        {
            var start = DlZipPath[DlZipSegment];
            var end = DlZipPath[DlZipSegment + 1];
            var length = Vector3.Distance(start, end);
            var left = DlZipDirection > 0 ? (1f - DlZipSegmentFraction) * length : DlZipSegmentFraction * length;

            if (distance < left)
            {
                DlZipSegmentFraction += DlZipDirection * (length > 0f ? distance / length : 0f);
                return true;
            }

            distance -= left;
            var next = DlZipSegment + DlZipDirection;

            if (next < 0 || next >= DlZipPath.Count - 1)
            {
                DlZipSegmentFraction = DlZipDirection > 0 ? 1f : 0f;
                return false;
            }

            DlZipSegment = next;
            DlZipSegmentFraction = DlZipDirection > 0 ? 0f : 1f;
        }

        return true;
    }

    private void DismountZipline(float speedScale, float upSpeed)
    {
        Velocity = (Horizontal(Velocity) * speedScale) + new Vector3(0f, 0f, upSpeed);
        ZiplineState = DeadlockZiplineState.None;
        DlZipPath = [];
        DlZipWaitForJumpRelease = true;
        OnGround = false;
        SlopeClipNormalZ = 1f;
        // Dropping off keeps the air from dragging the hero, which the game does for a couple of seconds
        DlLaunched = speedScale < DlZipDismountSpeedScale;
    }
}
