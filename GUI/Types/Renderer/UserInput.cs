namespace GUI.Types.Renderer;

internal class UserInput
{
    private const float MovementSpeed = 200f; // WASD movement, per second
    private const float AltMovementSpeed = 10f; // Holding shift or alt movement

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

    private float Uptime;
    private float TransitionDuration = 1.5f;
    private float TransitionEndTime = -1f;
    private CameraLite StartingCamera;
    public Camera Camera;
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

    private TrackedKeys PreviousKeys;

    /// <summary>
    /// Force an input update on the next tick.
    /// </summary>
    public bool ForceUpdate { get => field || TransitionEndTime > Uptime; set; } = true;
    public bool EnableMouseLook { get; set; } = true;
    private Vector2 MouseDelta2D;
    private Vector2 MouseDeltaPitchYaw;

    public UserInput()
    {
        Camera = new Camera();
    }

    public void Tick(float deltaTime, TrackedKeys keyboardState, Vector2 mouseDelta, Camera renderCamera)
    {
        Uptime += deltaTime;
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
            var holdingAlt = keyboardState.HasFlag(TrackedKeys.Alt);
            var justPressedAlt = holdingAlt && !PreviousKeys.HasFlag(TrackedKeys.Alt);

            if (!holdingAlt)
            {
                OrbitTarget = null;
            }
            else if (justPressedAlt)
            {
                OrbitTarget = null;

                var traceResult = PhysicsWorld?.TraceRay(Camera.Location, Camera.Location + Camera.Forward * 10000f);
                if (traceResult is { Hit: true, HitPosition: var hitPosition })
                {
                    OrbitTarget = hitPosition;
                }

            }

        }

        if (OrbitMode)
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

    public void SaveCameraForTransition(float transitionDuration = 1.5f)
    {
        StartingCamera = new(Camera.Location, Camera.Pitch, Camera.Yaw);
        TransitionDuration = transitionDuration;
        TransitionEndTime = Uptime + transitionDuration;
    }

    private CameraLite GetInterpolatedCamera()
    {
        if (TransitionEndTime < Uptime)
        {
            return new(Camera.Location, Camera.Pitch, Camera.Yaw);
        }

        var time = 1f - MathF.Pow((TransitionEndTime - Uptime) / TransitionDuration, 5f); // easeOutQuint

        var location = Vector3.Lerp(StartingCamera.Location, Camera.Location, time);
        var pitch = float.Lerp(StartingCamera.Pitch, Camera.Pitch, time);
        var yaw = float.Lerp(StartingCamera.Yaw, Camera.Yaw, time);

        return new(location, pitch, yaw);
    }

    private void HandleOrbitControls(float deltaTime, TrackedKeys keyboardState)
    {
        if (keyboardState.HasFlag(TrackedKeys.MouseRight))
        {
            var speed = deltaTime * OrbitDistance / 2;
            var panOffset = Camera.Right * speed * -MouseDelta2D.X;

            OrbitTarget += panOffset;
            Camera.Location += panOffset;
        }

        if (keyboardState.HasFlag(TrackedKeys.MouseLeft))
        {
            Camera.Yaw -= MouseDeltaPitchYaw.Y;
            Camera.Pitch -= MouseDeltaPitchYaw.X;
        }

        if (keyboardState.HasFlag(TrackedKeys.Forward))
        {
            OrbitZoom(-deltaTime * 10);
        }

        if (keyboardState.HasFlag(TrackedKeys.Back))
        {
            OrbitZoom(deltaTime * 10);
        }

        Camera.RecalculateDirectionVectors();
        var forward = Camera.Forward;
        var target = OrbitTarget ?? Vector3.Zero;
        Camera.Location = target - forward * OrbitDistance;
    }

    private void HandleFreeFlightControls(float deltaTime, TrackedKeys keyboardState)
    {
        if (keyboardState.HasFlag(TrackedKeys.Shift))
        {
            // Camera truck and pedestal movement (blender calls this pan)
            var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

            var location = Camera.Location;

            Camera.Location += Camera.Up * speed * -MouseDelta2D.Y;
            Camera.Location += Camera.Right * speed * MouseDelta2D.X;
            return;
        }

        // Use the keyboard state to update position
        HandleKeyboardInput(deltaTime, keyboardState);

        Camera.Pitch -= MouseDeltaPitchYaw.X;
        Camera.Yaw -= MouseDeltaPitchYaw.Y;
    }


    /// <summary>
    /// Moves the camera by the specified amounts in camera space.
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
        var speed = MovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

        if (keyboardState.HasFlag(TrackedKeys.Forward))
        {
            Camera.Location += Camera.Forward * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Back))
        {
            Camera.Location -= Camera.Forward * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Right))
        {
            Camera.Location += Camera.Right * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Left))
        {
            Camera.Location -= Camera.Right * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Down))
        {
            Camera.Location += new Vector3(0, 0, -speed);
        }

        if (keyboardState.HasFlag(TrackedKeys.Up))
        {
            Camera.Location += new Vector3(0, 0, speed);
        }
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
