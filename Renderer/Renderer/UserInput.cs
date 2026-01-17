namespace ValveResourceFormat.Renderer;

public class UserInput
{
    private const float MovementSpeed = 250f; // WASD movement, per second
    private const float AltMovementSpeed = 10f; // Holding shift or alt movement
    private const float Acceleration = 15f; // Acceleration multiplier
    private const float Deceleration = 20f; // Deceleration multiplier

    private readonly float[] SpeedModifiers =
    [
        0.1f,
        0.3f,
        0.5f,
        0.8f,
        1.0f,
        1.5f,
        2.0f,
        5.0f,
        10.0f,
    ];
    private int CurrentSpeedModifier = 4;

    public record struct CameraLite(Vector3 Location, float Pitch, float Yaw);

    private readonly Renderer Renderer;
    private float TransitionDuration = 1.5f;
    private float TransitionEndTime = -1f;
    private CameraLite StartingCamera;
    public Camera Camera { get; }
    public Rubikon? PhysicsWorld { get; set; }

    // Orbit controls
    public bool OrbitMode => OrbitTarget != null;
    public bool OrbitModeAlways { get; set; }
    public Vector3? OrbitTarget
    {
        get;
        set
        {
            field = value;
            OrbitDistance = Vector3.Distance(Camera.Location, value ?? Vector3.Zero);
        }
    }

    public float OrbitDistance { get; private set; }
    private const float MinOrbitDistance = 1f;
    private const float MaxOrbitDistance = 10000f;
    private const float OrbitZoomSpeed = 0.1f;

    private readonly PlayerMovement PlayerMovement;
    public bool NoClip { get; private set; } = true;

    private TrackedKeys Keys;
    private TrackedKeys PreviousKeys;
    public Vector3 Velocity { get; private set; }

    /// <summary>
    /// Force an input update on the next tick.
    /// </summary>
    public bool ForceUpdate { get => field || TransitionEndTime > Renderer.Uptime; set; } = true;
    public bool EnableMouseLook { get; set; } = true;
    private Vector2 MouseDelta2D;
    private Vector2 MouseDeltaPitchYaw;

    public UserInput(Renderer renderer)
    {
        Renderer = renderer;
        Camera = new Camera(renderer.RendererContext);
        PlayerMovement = new PlayerMovement(this);
    }

    /// <summary>
    /// Checks if a key is currently being held down.
    /// </summary>
    public bool Holding(TrackedKeys key) => (Keys & key) != 0;

    /// <summary>
    /// Checks if a key was just pressed this frame (pressed now but not last frame).
    /// </summary>
    public bool Pressed(TrackedKeys key) => (Keys & ~PreviousKeys & key) != 0;

    /// <summary>
    /// Checks if a key was just released this frame (not pressed now but was pressed last frame).
    /// </summary>
    private bool Released(TrackedKeys key) => (PreviousKeys & ~Keys & key) != 0;

    public void Tick(float deltaTime, TrackedKeys keyboardState, Vector2 mouseDelta, Camera renderCamera)
    {
        Keys = keyboardState;
        ForceUpdate = false;

        if (!EnableMouseLook)
        {
            mouseDelta = new Vector2(0, 0);
        }

        MouseDelta2D = mouseDelta;
        Camera.RecalculateDirectionVectors();

        // Full width of the screen is a 1 PI (180deg)
        MouseDeltaPitchYaw = new(
            MathF.PI / renderCamera.AspectRatio * mouseDelta.Y / renderCamera.WindowSize.Y,
            MathF.PI * mouseDelta.X / renderCamera.WindowSize.X
        );

        if (!OrbitModeAlways)
        {
            if (!Holding(TrackedKeys.Alt))
            {
                OrbitTarget = null;
            }
            else if (Pressed(TrackedKeys.Alt))
            {
                OrbitTarget = null;

                var traceResult = PhysicsWorld?.TraceRay(Camera.Location, Camera.Location + Camera.Forward * 10000f);
                if (traceResult is { Hit: true, HitPosition: var hitPosition })
                {
                    OrbitTarget = hitPosition;
                }

            }
        }

        var wasClipping = !NoClip;
        if (Pressed(TrackedKeys.X))
        {
            NoClip = !NoClip;
            PlayerMovement.Initialize = !NoClip;
        }

        if (Pressed(TrackedKeys.Escape))
        {
            NoClip = true;
        }

        if (wasClipping && NoClip)
        {
            MoveCamera(0, 32, 0, true);
            CurrentSpeedModifier = 7;
        }

        if (!NoClip)
        {
            PlayerMovement.ProcessMovement(this, Camera, deltaTime);
            Velocity = PlayerMovement.Velocity;
            Camera.Pitch -= MouseDeltaPitchYaw.X;
            Camera.Yaw -= MouseDeltaPitchYaw.Y;
            Camera.ClampRotation();
        }
        else if (OrbitMode)
        {
            HandleOrbitControls(deltaTime, keyboardState);
        }
        else
        {
            HandleFreeFlightControls(deltaTime, keyboardState);
        }

        var finalCamera = GetInterpolatedCamera();

        renderCamera.SetLocationPitchYaw(finalCamera.Location, finalCamera.Pitch, finalCamera.Yaw);
        renderCamera.ClampRotation();

        PreviousKeys = keyboardState;
    }

    private CameraLite CameraPositionAngles
        => new(Camera.Location, Camera.Pitch, Camera.Yaw);

    public void SaveCameraForTransition(float transitionDuration = 1.5f)
    {
        NoClip = true;

        StartingCamera = GetInterpolatedCamera();
        TransitionDuration = transitionDuration;
        TransitionEndTime = Renderer.Uptime + transitionDuration;
    }

    private CameraLite GetInterpolatedCamera()
    {
        if (TransitionEndTime < Renderer.Uptime)
        {
            return CameraPositionAngles;
        }

        var time = 1f - MathF.Pow((TransitionEndTime - Renderer.Uptime) / TransitionDuration, 5f); // easeOutQuint

        var location = Vector3.Lerp(StartingCamera.Location, Camera.Location, time);
        var pitch = float.Lerp(StartingCamera.Pitch, Camera.Pitch, time);
        var yaw = float.Lerp(StartingCamera.Yaw, Camera.Yaw, time);

        return new(location, pitch, yaw);
    }

    private void HandleOrbitControls(float deltaTime, TrackedKeys keyboardState)
    {
        var previousCamera = CameraPositionAngles;

        if ((keyboardState & TrackedKeys.MouseRight) != 0)
        {
            var speed = deltaTime * OrbitDistance / 2;
            var panOffset = Camera.Right * speed * -MouseDelta2D.X;

            OrbitTarget += panOffset;
            Camera.Location += panOffset;
        }

        if ((keyboardState & TrackedKeys.MouseLeft) != 0)
        {
            Camera.Yaw -= MouseDeltaPitchYaw.Y;
            Camera.Pitch -= MouseDeltaPitchYaw.X;
            Camera.ClampRotation();
        }

        if ((keyboardState & TrackedKeys.Forward) != 0)
        {
            OrbitZoom(-deltaTime * 10);
        }

        if ((keyboardState & TrackedKeys.Back) != 0)
        {
            OrbitZoom(deltaTime * 10);
        }

        Camera.RecalculateDirectionVectors();
        var forward = Camera.Forward;
        var target = OrbitTarget ?? Vector3.Zero;
        var newLocation = target - forward * OrbitDistance;

        Camera.Location = newLocation;

        var (clipped, clippedPos, clippedTime) = ClipOrbitMovement(previousCamera.Location, newLocation);
        if (clipped)
        {
            Camera.Location = clippedPos;
            Camera.Yaw = float.Lerp(previousCamera.Yaw, Camera.Yaw, clippedTime);
            Camera.Pitch = float.Lerp(previousCamera.Pitch, Camera.Pitch, clippedTime);

            var direction = clippedPos - target;
            OrbitDistance = direction.Length();
        }
    }

    private (bool Clipped, Vector3 ClipPosition, float ImpactTime) ClipOrbitMovement(Vector3 fromLocation, Vector3 toLocation)
    {
        const float minDistance = 8f;
        const float margin = 0.01f;

        if (PhysicsWorld != null)
        {
            var movementDelta = toLocation - fromLocation;
            var movementDistance = movementDelta.Length();

            if (movementDistance >= 0.001f)
            {
                var direction = Vector3.Normalize(movementDelta);

                var extendedRay = toLocation + direction * minDistance;
                var extendedDistance = movementDistance + minDistance;

                var traceResult = PhysicsWorld.TraceRay(fromLocation, extendedRay);
                if (traceResult is { Hit: true, HitPosition: var hitPosition, Distance: var distance })
                {
                    return (true, hitPosition - (direction * (minDistance + margin)), distance / extendedDistance);
                }
            }
        }

        return (false, toLocation, 1f);
    }

    private void HandleFreeFlightControls(float deltaTime, TrackedKeys keyboardState)
    {
        if ((keyboardState & TrackedKeys.Shift) != 0)
        {
            // Camera truck and pedestal movement (blender calls this pan)
            var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];
            var screenRight = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, Camera.Forward));
            var screenUp = Vector3.Cross(Camera.Forward, screenRight);

            Camera.Location -= screenRight * speed * MouseDelta2D.X;
            Camera.Location -= screenUp * speed * MouseDelta2D.Y;
            return;
        }

        // Use the keyboard state to update position
        HandleKeyboardInput(deltaTime, keyboardState);

        Camera.Pitch -= MouseDeltaPitchYaw.X;
        Camera.Yaw -= MouseDeltaPitchYaw.Y;
        Camera.ClampRotation();
    }


    /// <summary>
    /// Moves the camera by the specified amounts in camera space.
    /// Why is this Y-Up?
    /// </summary>
    public void MoveCamera(float x, float y, float z, bool transition = false)
    {
        Camera.RecalculateDirectionVectors();
        var forward = Camera.Forward;
        var right = Camera.Right;
        var up = Camera.Up;

        var movement = right * x + up * y + forward * z;
        var newLocation = Camera.Location + movement;

        if (transition)
        {
            SaveCameraForTransition();
        }

        Camera.Location = newLocation;
    }

    public float OnMouseWheel(float delta)
    {
        if (OrbitMode)
        {
            OrbitZoom(-delta * 0.01f);
            return OrbitDistance;
        }

        if (delta > 0)
        {
            CurrentSpeedModifier += 1;

            if (CurrentSpeedModifier >= SpeedModifiers.Length)
            {
                CurrentSpeedModifier = SpeedModifiers.Length - 1;
            }
        }
        else
        {
            CurrentSpeedModifier -= 1;

            if (CurrentSpeedModifier < 0)
            {
                CurrentSpeedModifier = 0;
            }
        }

        return SpeedModifiers[CurrentSpeedModifier];
    }

    private void HandleKeyboardInput(float deltaTime, TrackedKeys keyboardState)
    {
        var maxSpeed = MovementSpeed * SpeedModifiers[CurrentSpeedModifier];
        var targetVelocity = Vector3.Zero;

        if ((keyboardState & TrackedKeys.Forward) != 0)
        {
            targetVelocity += Camera.Forward * maxSpeed;
        }

        if ((keyboardState & TrackedKeys.Back) != 0)
        {
            targetVelocity -= Camera.Forward * maxSpeed;
        }

        if ((keyboardState & TrackedKeys.Right) != 0)
        {
            targetVelocity += Camera.Right * maxSpeed;
        }

        if ((keyboardState & TrackedKeys.Left) != 0)
        {
            targetVelocity -= Camera.Right * maxSpeed;
        }

        if ((keyboardState & TrackedKeys.Down) != 0)
        {
            targetVelocity += new Vector3(0, 0, -maxSpeed);
        }

        if ((keyboardState & TrackedKeys.Up) != 0)
        {
            targetVelocity += new Vector3(0, 0, maxSpeed);
        }

        // Apply acceleration or deceleration
        var hasInput = targetVelocity.LengthSquared() > 0.01f;
        var smoothingFactor = hasInput ? Acceleration : Deceleration;
        Velocity = Vector3.Lerp(Velocity, targetVelocity, 1f - MathF.Exp(-smoothingFactor * deltaTime));

        // Apply velocity to camera position
        Camera.Location += Velocity * deltaTime;
    }

    public void OrbitZoom(float delta)
    {
        if (!OrbitMode)
        {
            return;
        }

        OrbitDistance *= 1f + delta * OrbitZoomSpeed;
        OrbitDistance = Math.Clamp(OrbitDistance, MinOrbitDistance, MaxOrbitDistance);
        SaveCameraForTransition(transitionDuration: 0.5f);
    }
}
