namespace GUI.Types.Renderer;

internal class UserInput
{
    private const float MovementSpeed = 300f; // WASD movement, per second
    private const float AltMovementSpeed = 10f; // Holding shift or alt movement

    private readonly float[] SpeedModifiers =
    [
        0.1f,
        0.5f,
        1.0f,
        2.0f,
        5.0f,
        10.0f,
    ];

    private int CurrentSpeedModifier = 2;

    public record struct CameraLite(Vector3 Location, float Pitch, float Yaw);

    private float Uptime;
    private float TransitionDuration = 1.5f;
    private float TransitionEndTime = -1f;
    private CameraLite StartingCamera;
    public Camera TargetCamera;

    // Orbit controls
    public bool OrbitMode { get; set; }
    public Vector3 OrbitTarget { get; set; }
    public float OrbitDistance { get; private set; }
    private const float MinOrbitDistance = 1f;
    private const float MaxOrbitDistance = 10000f;
    private const float OrbitZoomSpeed = 0.1f;

    private TrackedKeys PreviousKeys;

    public bool ForceUpdate { get; set; } = true;
    private Vector2 MouseDelta2D;
    private Vector2 MouseDeltaPitchYaw;

    public UserInput()
    {
        TargetCamera = new Camera();
        //OrbitDistance = Vector3.Distance(Location, OrbitTarget);
    }

    public void Tick(float deltaTime, TrackedKeys keyboardState, Vector2 mouseDelta, Camera renderCamera)
    {
        Uptime += deltaTime;
        ForceUpdate = false;

        MouseDelta2D = mouseDelta;

        // Full width of the screen is a 1 PI (180deg)
        MouseDeltaPitchYaw = new(
            MathF.PI / renderCamera.AspectRatio * mouseDelta.Y / renderCamera.WindowSize.Y,
            MathF.PI * mouseDelta.X / renderCamera.WindowSize.X
        );

        if (keyboardState.HasFlag(TrackedKeys.Alt))
        {
            EnableOrbitMode();
        }
        else
        {
            DisableOrbitMode();
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

    public void SaveCurrentForTransition(float transitionDuration = 1.5f)
    {
        StartingCamera = new(TargetCamera.Location, TargetCamera.Pitch, TargetCamera.Yaw);
        TransitionDuration = transitionDuration;
        TransitionEndTime = Uptime + transitionDuration;
    }

    private CameraLite GetInterpolatedCamera()
    {
        if (TransitionEndTime < Uptime)
        {
            return new(TargetCamera.Location, TargetCamera.Pitch, TargetCamera.Yaw);
        }

        var time = 1f - MathF.Pow((TransitionEndTime - Uptime) / TransitionDuration, 5f); // easeOutQuint

        var location = Vector3.Lerp(StartingCamera.Location, TargetCamera.Location, time);
        var pitch = float.Lerp(StartingCamera.Pitch, TargetCamera.Pitch, time);
        var yaw = float.Lerp(StartingCamera.Yaw, TargetCamera.Yaw, time);

        return new(location, pitch, yaw);
    }

    public void SetOrbitTarget(Vector3 target)
    {
        OrbitTarget = target;
        OrbitDistance = Vector3.Distance(TargetCamera.Location, OrbitTarget);
    }

    public void EnableOrbitMode(Vector3? target = null)
    {
        OrbitMode = true;

        if (target != null)
        {
            OrbitTarget = target.Value;
        }
        else
        {
            //TODO: Use traces to find the point at which we intersect geo, and use that as orbit target
            OrbitTarget = TargetCamera.Location + TargetCamera.GetForwardVector() * 100;
        }

        OrbitDistance = Vector3.Distance(TargetCamera.Location, OrbitTarget);
    }

    public void DisableOrbitMode()
    {
        OrbitMode = false;
    }

    private void HandleOrbitControls(float deltaTime, TrackedKeys keyboardState)
    {
        if (keyboardState.HasFlag(TrackedKeys.MouseRight))
        {
            var speed = deltaTime * OrbitDistance / 2;
            var panOffset = TargetCamera.GetRightVector() * speed * -MouseDelta2D.X;

            OrbitTarget += panOffset;
            TargetCamera.Location += panOffset;
        }

        if (keyboardState.HasFlag(TrackedKeys.MouseLeft))
        {
            TargetCamera.Yaw -= MouseDeltaPitchYaw.Y;
            TargetCamera.Pitch -= MouseDeltaPitchYaw.X;

            UpdateOrbitLocation();
        }

        if (keyboardState.HasFlag(TrackedKeys.Forward))
        {
            OrbitZoom(-deltaTime * 10);
        }

        if (keyboardState.HasFlag(TrackedKeys.Back))
        {
            OrbitZoom(deltaTime * 10);
        }
    }

    private void HandleFreeFlightControls(float deltaTime, TrackedKeys keyboardState)
    {
        if (keyboardState.HasFlag(TrackedKeys.Shift))
        {
            // Camera truck and pedestal movement (blender calls this pan)
            var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

            var location = TargetCamera.Location;

            TargetCamera.Location += TargetCamera.GetUpVector() * speed * -MouseDelta2D.Y;
            TargetCamera.Location += TargetCamera.GetRightVector() * speed * MouseDelta2D.X;
        }
        else
        {
            // Use the keyboard state to update position
            HandleKeyboardInput(deltaTime, keyboardState);

            TargetCamera.Pitch -= MouseDeltaPitchYaw.X;
            TargetCamera.Yaw -= MouseDeltaPitchYaw.Y;
        }
    }

    public float ModifySpeed(float subRange)
    {
        subRange = Math.Clamp(subRange, 0, 1.0f);
        CurrentSpeedModifier = (int)MathF.Round(subRange * (SpeedModifiers.Length - 1));
        return SpeedModifiers[CurrentSpeedModifier];
    }

    public float OnMouseWheel(float delta)
    {
        if (OrbitMode)
        {
            OrbitZoom(-delta * 0.01f);
            return OrbitDistance;
        }
        else
        {
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
    }

    private void HandleKeyboardInput(float deltaTime, TrackedKeys keyboardState)
    {
        var speed = MovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

        if (keyboardState.HasFlag(TrackedKeys.Forward))
        {
            TargetCamera.Location += TargetCamera.GetForwardVector() * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Back))
        {
            TargetCamera.Location -= TargetCamera.GetForwardVector() * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Right))
        {
            TargetCamera.Location += TargetCamera.GetRightVector() * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Left))
        {
            TargetCamera.Location -= TargetCamera.GetRightVector() * speed;
        }

        if (keyboardState.HasFlag(TrackedKeys.Down))
        {
            TargetCamera.Location += new Vector3(0, 0, -speed);
        }

        if (keyboardState.HasFlag(TrackedKeys.Up))
        {
            TargetCamera.Location += new Vector3(0, 0, speed);
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
        SaveCurrentForTransition(transitionDuration: 0.5f);
        UpdateOrbitLocation();
    }

    private void UpdateOrbitLocation()
    {
        var forward = TargetCamera.GetForwardVector();
        TargetCamera.Location = OrbitTarget - forward * OrbitDistance;
    }
}
