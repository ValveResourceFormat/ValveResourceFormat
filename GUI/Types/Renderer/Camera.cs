using System.Drawing;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class Camera
    {
        private const float MovementSpeed = 300f; // WASD movement, per second

        public readonly FpsMovement FpsMovement = new();

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

        private const float TransitionDuration = 1.5f;
        private float TransitionEndTime = -1f;
        private float TransitionOldPitch;
        private float TransitionOldYaw;
        private Vector3 TransitionOldLocation;

        public Vector3 Location { get; private set; }
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }

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
        }


        private (Vector3 Location, float Pitch, float Yaw) GetCurrentLocationWithTransition(float uptime)
        {
            if (TransitionEndTime < uptime)
            {
                return (Location, Pitch, Yaw);
            }

            var time = 1f - MathF.Pow((TransitionEndTime - uptime) / TransitionDuration, 5f); // easeOutQuint

            var location = Vector3.Lerp(TransitionOldLocation, Location, time);
            var pitch = float.Lerp(TransitionOldPitch, Pitch, time);
            var yaw = float.Lerp(TransitionOldYaw, Yaw, time);

            return (location, pitch, yaw);
        }

        public void RecalculateMatrices(float uptime)
        {
            var (location, pitch, yaw) = GetCurrentLocationWithTransition(uptime);

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
        }

        public void SetLocation(Vector3 location)
        {
            Location = location;
        }

        public void SetLocationPitchYaw(Vector3 location, float pitch, float yaw)
        {
            Location = location;
            Pitch = pitch;
            Yaw = yaw;
        }

        public void LookAt(Vector3 target)
        {
            var dir = Vector3.Normalize(target - Location);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);

            ClampRotation();
        }

        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);
        }

        public void SaveCurrentForTransition(float uptime)
        {
            (TransitionOldLocation, TransitionOldPitch, TransitionOldYaw) = GetCurrentLocationWithTransition(uptime);
            TransitionEndTime = uptime + TransitionDuration;
        }

        public void Tick(float deltaTime, TrackedKeys keyboardState, Point mouseDelta)
        {
            Location = FpsMovement.ProcessMovement(Location, keyboardState, deltaTime, Pitch, Yaw);

            Yaw -= MathF.PI * mouseDelta.X / WindowSize.X;
            Pitch -= MathF.PI / AspectRatio * mouseDelta.Y / WindowSize.Y;

            /*
            if ((keyboardState & TrackedKeys.Control) > 0)
            {
                // Disable camera movement while holding control
                // This is used by single node viewer to change sun angle,
                // and if you press Ctrl+W, the tab will close anyway
                return;
            }

            if ((keyboardState & TrackedKeys.Shift) > 0)
            {
                // Camera truck and pedestal movement (blender calls this pan)
                var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

                Location += GetUpVector() * speed * -mouseDelta.Y;
                Location += GetRightVector() * speed * mouseDelta.X;
            }
            else if ((keyboardState & TrackedKeys.Alt) > 0)
            {
                // Move forward or backwards when holding alt
                var totalDelta = mouseDelta.X + (mouseDelta.Y * -1);
                var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

                Location += GetForwardVector() * totalDelta * speed;
            }
            else
            {
                // Use the keyboard state to update position
                HandleKeyboardInput(deltaTime, keyboardState);

                // Full width of the screen is a 1 PI (180deg)
                Yaw -= MathF.PI * mouseDelta.X / WindowSize.X;
                Pitch -= MathF.PI / AspectRatio * mouseDelta.Y / WindowSize.Y;
            }
            */

            ClampRotation();
        }

        public float ModifySpeed(float subRange)
        {
            subRange = Math.Clamp(subRange, 0, 1.0f);
            CurrentSpeedModifier = (int)MathF.Round(subRange * (SpeedModifiers.Length - 1));
            return SpeedModifiers[CurrentSpeedModifier];
        }

        public float ModifySpeed(bool increase)
        {
            if (increase)
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

            if ((keyboardState & TrackedKeys.Forward) > 0)
            {
                Location += GetForwardVector() * speed;
            }

            if ((keyboardState & TrackedKeys.Back) > 0)
            {
                Location -= GetForwardVector() * speed;
            }

            if ((keyboardState & TrackedKeys.Right) > 0)
            {
                Location += GetRightVector() * speed;
            }

            if ((keyboardState & TrackedKeys.Left) > 0)
            {
                Location -= GetRightVector() * speed;
            }

            if ((keyboardState & TrackedKeys.Down) > 0)
            {
                Location += new Vector3(0, 0, -speed);
            }

            if ((keyboardState & TrackedKeys.Up) > 0)
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
