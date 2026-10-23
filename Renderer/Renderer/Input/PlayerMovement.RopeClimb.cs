using ValveResourceFormat.Renderer.Entities;

namespace ValveResourceFormat.Renderer.Input;

// Deadlock ropes: holding jump inside a rope's volume grabs it. Forward climbs, back slides down faster and
// faster, jump leaps off the way the view points and duck lets go.
public partial class PlayerMovement
{
    private const float DlRopeHoldTime = 0.15f;          // m_flMinButtonHoldTimeToActivate
    private const float DlRopeBottomOffset = 60f;        // m_flBottomOffset
    private const float DlRopeTopOffset = 70f;           // m_flAttachOffset: the hands hold this far above the feet
    private const float DlRopeDownAccelTime = 1f;        // m_flClimbDownAccelTime
    private const float DlRopeJumpOffHorizontal = 400f;
    private const float DlRopeJumpOffVertical = 250f;
    private const float DlRopeDuckOffHorizontal = 400f;
    private const float DlRopeDuckOffVertical = 100f;

    private Vector3 DlRopeBottom;
    private Vector3 DlRopeTop;
    private float DlRopeHeld;
    private float DlRopeDownTime;

    /// <summary>Gets whether the hero is on a rope.</summary>
    public bool IsOnRope { get; private set; }

    /// <summary>Gets which way the hero moves along the rope: 1 up, -1 down, 0 holding still.</summary>
    public int RopeDirection { get; private set; }

    /// <summary>Grabs a rope the hero is inside the volume of, once jump has been held long enough.</summary>
    private bool TryGrabRope(Vector3 position, Vector3 halfExtents, float deltaTime)
    {
        if (!Input.Holding(TrackedKeys.Space))
        {
            DlZipWaitForJumpRelease = false;
            DlRopeHeld = 0f;
            return false;
        }

        if (DlZipWaitForJumpRelease || Input.EntitySystem is not { } entities)
        {
            DlRopeHeld = 0f;
            return false;
        }

        foreach (var entity in entities.Entities)
        {
            if (entity is not CitadelTriggerClimbRope { Volume: { } volume } || !volume.Contains(position))
            {
                continue;
            }

            DlRopeHeld += deltaTime;

            if (DlRopeHeld < DlRopeHoldTime)
            {
                return false;
            }

            var center = volume.Center;
            DlRopeBottom = new Vector3(center.X, center.Y, volume.Min.Z + DlRopeBottomOffset);
            DlRopeTop = new Vector3(center.X, center.Y, volume.Max.Z - DlRopeTopOffset);
            DlRopeHeld = 0f;
            DlRopeDownTime = 0f;
            IsOnRope = true;
            OnGround = false;
            Velocity = Vector3.Zero;
            DlDashTimer = -1f;
            DlSlideTimer = -1f;
            DlAirDashTimer = -1f;
            DlZipWaitForJumpRelease = true;

            // The hero hangs from the rope by the middle of its hull
            TracePosition = new Vector3(center.X, center.Y, Math.Clamp(position.Z - halfExtents.Z, DlRopeBottom.Z, DlRopeTop.Z) + halfExtents.Z);
            return true;
        }

        DlRopeHeld = 0f;
        return false;
    }

    /// <summary>One frame on a rope: the hero is carried along it, and nothing it would collide with stops it.</summary>
    private void DeadlockRopeFrame(Camera camera, float deltaTime)
    {
        var halfExtents = HullHalfExtents;
        var (forward, _) = ViewBasis();

        if (!Input.Holding(TrackedKeys.Space))
        {
            DlZipWaitForJumpRelease = false;
        }

        if (Input.Pressed(TrackedKeys.Space) && !DlZipWaitForJumpRelease)
        {
            LeaveRope(forward * DlRopeJumpOffHorizontal + new Vector3(0f, 0f, DlRopeJumpOffVertical));
            NoteMove(DeadlockMove.GroundJump, forward);
            return;
        }

        if (Input.Pressed(TrackedKeys.Control))
        {
            LeaveRope(forward * DlRopeDuckOffHorizontal + new Vector3(0f, 0f, DlRopeDuckOffVertical));
            return;
        }

        RopeDirection = (Input.Holding(TrackedKeys.W) ? 1 : 0) - (Input.Holding(TrackedKeys.S) ? 1 : 0);

        // Down starts as quick as climbing and doubles over a second
        DlRopeDownTime = RopeDirection < 0 ? DlRopeDownTime + deltaTime : 0f;
        var speed = RopeDirection < 0
            ? hero.RunSpeed * (1f + MathUtils.Saturate(DlRopeDownTime / DlRopeDownAccelTime))
            : hero.RunSpeed;

        var feet = TracePosition.Z - halfExtents.Z + (RopeDirection * speed * deltaTime);

        // Sliding off the bottom lets go
        if (feet < DlRopeBottom.Z && RopeDirection < 0)
        {
            LeaveRope(Vector3.Zero);
            return;
        }

        feet = Math.Clamp(feet, DlRopeBottom.Z, DlRopeTop.Z);
        TracePosition = DlRopeBottom with { Z = feet + halfExtents.Z };
        TracePositionSmooth = TracePosition;
        Velocity = new Vector3(0f, 0f, RopeDirection * speed);

        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = 0f;
    }

    private void LeaveRope(Vector3 velocity)
    {
        IsOnRope = false;
        RopeDirection = 0;
        DlZipWaitForJumpRelease = true;
        Velocity = velocity;
        OnGround = false;
        SlopeClipNormalZ = 1f;
    }
}
