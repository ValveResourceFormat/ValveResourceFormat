using System.Drawing;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class Camera
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

        private float Uptime;
        private float TransitionDuration = 1.5f;
        private float TransitionEndTime = -1f;
        private float TransitionOldPitch;
        private float TransitionOldYaw;
        private Vector3 TransitionOldLocation;

        public Vector3 Location { get; private set; }
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }

        public bool EnableMouseLook { get; set; }

        // Orbit controls
        public bool OrbitMode { get; set; }
        public bool OrbitModeAlways { get; set; }
        public Vector3 OrbitTarget { get; set; }
        public float OrbitDistance { get; private set; }
        private const float MinOrbitDistance = 1f;
        private const float MaxOrbitDistance = 10000f;
        private const float OrbitZoomSpeed = 0.1f;

        private Matrix4x4 ProjectionMatrix;
        public Matrix4x4 CameraViewMatrix { get; private set; }
        public Matrix4x4 ViewProjectionMatrix { get; private set; }
        public Frustum ViewFrustum { get; } = new Frustum();

        public Vector2 WindowSize;
        private float AspectRatio;

        public Camera()
        {
            Location = Vector3.One;
            SetViewportSize(16, 9);
            LookAt(Vector3.Zero);

            OrbitDistance = Vector3.Distance(Location, OrbitTarget);
        }


        private (Vector3 Location, float Pitch, float Yaw) GetCurrentLocationWithTransition()
        {
            if (TransitionEndTime < Uptime)
            {
                return (Location, Pitch, Yaw);
            }

            var time = 1f - MathF.Pow((TransitionEndTime - Uptime) / TransitionDuration, 5f); // easeOutQuint

            var location = Vector3.Lerp(TransitionOldLocation, Location, time);
            var pitch = float.Lerp(TransitionOldPitch, Pitch, time);
            var yaw = float.Lerp(TransitionOldYaw, Yaw, time);

            return (location, pitch, yaw);
        }

        public void RecalculateMatrices(float uptime)
        {
            Uptime = uptime;
            var (location, pitch, yaw) = GetCurrentLocationWithTransition();

            var yawSin = MathF.Sin(yaw);
            var yawCos = MathF.Cos(yaw);
            var pitchSin = MathF.Sin(pitch);
            var pitchCos = MathF.Cos(pitch);
            var forward = new Vector3(yawCos * pitchCos, yawSin * pitchCos, pitchSin);

            CameraViewMatrix = Matrix4x4.CreateLookAt(location, location + forward, Vector3.UnitZ);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        // Calculate forward vector from pitch and yaw
        public Vector3 GetForwardVector()
        {
            var yawSin = MathF.Sin(Yaw);
            var yawCos = MathF.Cos(Yaw);
            var pitchSin = MathF.Sin(Pitch);
            var pitchCos = MathF.Cos(Pitch);
            return new Vector3(yawCos * pitchCos, yawSin * pitchCos, pitchSin);
        }

        private Vector3 GetUpVector()
        {
            var yawSin = MathF.Sin(Yaw);
            var yawCos = MathF.Cos(Yaw);
            var pitchSin = MathF.Sin(Pitch);
            var pitchCos = MathF.Cos(Pitch);
            return new Vector3(yawCos * pitchSin, yawSin * pitchSin, pitchCos);
        }

        private Vector3 GetRightVector()
        {
            const float piOver2 = MathF.PI / 2f;
            return new Vector3(MathF.Cos(Yaw - piOver2), MathF.Sin(Yaw - piOver2), 0);
        }

        public void SetViewConstants(Buffers.ViewConstants viewConstants)
        {
            viewConstants.WorldToProjection = ViewProjectionMatrix;
            viewConstants.WorldToView = CameraViewMatrix;
            viewConstants.ViewToProjection = ProjectionMatrix;
            viewConstants.CameraPosition = Location;

            Matrix4x4.Invert(ProjectionMatrix, out viewConstants.ProjectionToWorld);
            viewConstants.InvProjRow3 = new Vector4(
                viewConstants.ProjectionToWorld.M14,
                viewConstants.ProjectionToWorld.M24,
                viewConstants.ProjectionToWorld.M34,
                viewConstants.ProjectionToWorld.M44
            );

            viewConstants.CameraDirWs = GetForwardVector();
            viewConstants.CameraUpDirWs = GetUpVector();

            // todo: these change per scene, move to the other buffer
            viewConstants.ViewportMinZ = 0.05f;
            viewConstants.ViewportMaxZ = 1.0f;
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            ProjectionMatrix = CreatePerspectiveFieldOfView_ReverseZ(GetFOV(), AspectRatio, 1.0f);
        }

        /// <inheritdoc cref="Matrix4x4.CreatePerspectiveFieldOfView"/>
        /// <remarks>Note: Reverse-Z. Far plane is swapped with near plane. Far plane is set to infinite.</remarks>
        private static Matrix4x4 CreatePerspectiveFieldOfView_ReverseZ(float fieldOfView, float aspectRatio, float nearPlaneDistance)
        {
            var height = 1.0f / MathF.Tan(fieldOfView * 0.5f);
            var width = height / aspectRatio;

            return new Matrix4x4
            {
                M11 = width,
                M22 = height,
                M33 = 0.0f,
                M34 = -1.0f,
                M43 = nearPlaneDistance
            };
        }

        public void CopyFrom(Camera fromOther)
        {
            AspectRatio = fromOther.AspectRatio;
            WindowSize = fromOther.WindowSize;
            Location = fromOther.Location;
            Pitch = fromOther.Pitch;
            Yaw = fromOther.Yaw;
            ProjectionMatrix = fromOther.ProjectionMatrix;
            CameraViewMatrix = fromOther.CameraViewMatrix;
            ViewProjectionMatrix = fromOther.ViewProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
            OrbitMode = fromOther.OrbitMode;
            OrbitTarget = fromOther.OrbitTarget;
            OrbitDistance = fromOther.OrbitDistance;
        }

        public void SetLocation(Vector3 location)
        {
            Location = location;
            if (OrbitMode)
            {
                OrbitDistance = Vector3.Distance(Location, OrbitTarget);
            }
        }

        public void SetLocationPitchYaw(Vector3 location, float pitch, float yaw)
        {
            Location = location;
            Pitch = pitch;
            Yaw = yaw;
            if (OrbitMode)
            {
                OrbitDistance = Vector3.Distance(Location, OrbitTarget);
            }
        }

        public void LookAt(Vector3 target)
        {
            var dir = Vector3.Normalize(target - Location);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);

            ClampRotation();
        }


        public void FrameObject(Vector3 objectPosition, float width, float height, float depth)
        {
            var fov = GetFOV();
            var halfFovVertical = fov * 0.5f;
            var halfFovHorizontal = MathF.Atan(MathF.Tan(halfFovVertical) * AspectRatio);

            var forward = GetForwardVector();
            var right = GetRightVector();
            var up = GetUpVector();

            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var halfDepth = depth * 0.5f;

            // this calculate the apparent size in screen space by projecting onto camera axis
            var maxHorizontalExtent = 0f;
            var maxVerticalExtent = 0f;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) != 0 ? halfWidth : -halfWidth,
                    (i & 2) != 0 ? halfHeight : -halfHeight,
                    (i & 4) != 0 ? halfDepth : -halfDepth
                );

                var horizontalDist = MathF.Abs(Vector3.Dot(corner, right));
                var verticalDist = MathF.Abs(Vector3.Dot(corner, up));

                maxHorizontalExtent = MathF.Max(maxHorizontalExtent, horizontalDist);
                maxVerticalExtent = MathF.Max(maxVerticalExtent, verticalDist);
            }

            var distanceForVerticalFov = maxVerticalExtent / MathF.Tan(halfFovVertical);
            var distanceForHorizontalFov = maxHorizontalExtent / MathF.Tan(halfFovHorizontal);

            var distance = MathF.Max(distanceForVerticalFov, distanceForHorizontalFov);

            Location = objectPosition - forward * distance;

            LookAt(objectPosition);
        }

        public void FrameObjectFromAngle(Vector3 objectPosition, float width, float height, float depth,
            float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
            ClampRotation();

            FrameObject(objectPosition, width, height, depth);
        }

        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);
        }

        public void SaveCurrentForTransition(float transitionDuration = 1.5f)
        {
            (TransitionOldLocation, TransitionOldPitch, TransitionOldYaw) = GetCurrentLocationWithTransition();
            TransitionDuration = transitionDuration;
            TransitionEndTime = Uptime + transitionDuration;
        }

        public void SetOrbitTarget(Vector3 target)
        {
            OrbitTarget = target;
            OrbitDistance = Vector3.Distance(Location, OrbitTarget);
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
                OrbitTarget = Location + GetForwardVector() * 100;
            }

            OrbitDistance = Vector3.Distance(Location, OrbitTarget);
        }

        public void DisableOrbitMode()
        {
            OrbitMode = false;
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
            var forward = GetForwardVector();
            Location = OrbitTarget - forward * OrbitDistance;
        }

        public void Tick(float deltaTime, TrackedKeys keyboardState, Point mouseDelta)
        {
            if (!EnableMouseLook)
            {
                mouseDelta = new Point(0, 0);
            }

            if (!OrbitModeAlways)
            {
                if (keyboardState.HasFlag(TrackedKeys.Alt))
                {
                    EnableOrbitMode();
                }
                else
                {
                    DisableOrbitMode();
                }
            }

            if (OrbitMode)
            {
                HandleOrbitMode(deltaTime, keyboardState, mouseDelta);
            }
            else
            {
                HandleFreeMode(deltaTime, keyboardState, mouseDelta);
            }

            ClampRotation();
        }

        private void HandleOrbitMode(float deltaTime, TrackedKeys keyboardState, Point mouseDelta)
        {
            if (keyboardState.HasFlag(TrackedKeys.MouseRight))
            {
                var speed = deltaTime * OrbitDistance / 2;
                var panOffset = GetRightVector() * speed * -mouseDelta.X;

                OrbitTarget += panOffset;
                Location += panOffset;
            }

            if (keyboardState.HasFlag(TrackedKeys.MouseLeft))
            {
                Yaw -= MathF.PI * mouseDelta.X / WindowSize.X;
                Pitch -= MathF.PI / AspectRatio * mouseDelta.Y / WindowSize.Y;

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

        private void HandleFreeMode(float deltaTime, TrackedKeys keyboardState, Point mouseDelta)
        {
            if (keyboardState.HasFlag(TrackedKeys.Shift))
            {
                // Camera truck and pedestal movement (blender calls this pan)
                var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

                Location += GetUpVector() * speed * -mouseDelta.Y;
                Location += GetRightVector() * speed * mouseDelta.X;
            }
            else
            {
                // Use the keyboard state to update position
                HandleKeyboardInput(deltaTime, keyboardState);

                // Full width of the screen is a 1 PI (180deg)
                Yaw -= MathF.PI * mouseDelta.X / WindowSize.X;
                Pitch -= MathF.PI / AspectRatio * mouseDelta.Y / WindowSize.Y;
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
                Location += GetForwardVector() * speed;
            }

            if (keyboardState.HasFlag(TrackedKeys.Back))
            {
                Location -= GetForwardVector() * speed;
            }

            if (keyboardState.HasFlag(TrackedKeys.Right))
            {
                Location += GetRightVector() * speed;
            }

            if (keyboardState.HasFlag(TrackedKeys.Left))
            {
                Location -= GetRightVector() * speed;
            }

            if (keyboardState.HasFlag(TrackedKeys.Down))
            {
                Location += new Vector3(0, 0, -speed);
            }

            if (keyboardState.HasFlag(TrackedKeys.Up))
            {
                Location += new Vector3(0, 0, speed);
            }
        }

        // Prevent camera from going upside-down
        private void ClampRotation()
        {
            const float PITCH_LIMIT = 89.5f * MathF.PI / 180f;

            if (Pitch >= PITCH_LIMIT)
            {
                Pitch = PITCH_LIMIT;
            }
            else if (Pitch <= -PITCH_LIMIT)
            {
                Pitch = -PITCH_LIMIT;
            }
        }

        private static float GetFOV()
        {
            return Settings.Config.FieldOfView * MathF.PI / 180f;
        }
    }
}
