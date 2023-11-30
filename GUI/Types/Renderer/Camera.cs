using System;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

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

        public Vector3 Location { get; private set; }
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }
        public float Scale { get; set; } = 1.0f;

        private Matrix4x4 ProjectionMatrix;
        public Matrix4x4 CameraViewMatrix { get; private set; }
        public Matrix4x4 ViewProjectionMatrix { get; private set; }
        public Frustum ViewFrustum { get; } = new Frustum();
        public PickingTexture Picker { get; set; }

        // Set from outside this class by forms code
        public bool MouseOverRenderArea { get; set; }

        private Vector2 WindowSize;
        private float AspectRatio;

        public bool MouseDragging { get; private set; }

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;

        private KeyboardState KeyboardState;

        public Camera()
        {
            Location = Vector3.One;
            LookAt(Vector3.Zero);
        }

        private void RecalculateMatrices()
        {
            CameraViewMatrix = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateLookAt(Location, Location + GetForwardVector(), Vector3.UnitZ);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        // Calculate forward vector from pitch and yaw
        private Vector3 GetForwardVector()
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
            return new Vector3(MathF.Cos(Yaw - OpenTK.MathHelper.PiOver2), MathF.Sin(Yaw - OpenTK.MathHelper.PiOver2), 0);
        }

        public void SetViewConstants(UniformBuffers.ViewConstants viewConstants)
        {
            viewConstants.WorldToProjection = ProjectionMatrix;
            viewConstants.WorldToView = CameraViewMatrix;
            viewConstants.ViewToProjection = ViewProjectionMatrix;
            viewConstants.CameraPosition = Location / Scale;
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(GetFOV(), AspectRatio, 1.0f, 20000.0f);

            RecalculateMatrices();

            Picker?.Resize(viewportWidth, viewportHeight);
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
        }

        public void SetScaledProjectionMatrix()
        {
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(GetFOV(), AspectRatio, 10f * Scale, 20000.0f * Scale);
        }

        public void SetLocation(Vector3 location)
        {
            Location = location;
            RecalculateMatrices();
        }

        public void SetLocationPitchYaw(Vector3 location, float pitch, float yaw)
        {
            Location = location;
            Pitch = pitch;
            Yaw = yaw;
            RecalculateMatrices();
        }

        public void LookAt(Vector3 target)
        {
            var dir = Vector3.Normalize(target - Location);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);

            ClampRotation();
            RecalculateMatrices();
        }

        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);

            RecalculateMatrices();
        }

        public void Tick(float deltaTime)
        {
            if (!MouseOverRenderArea)
            {
                return;
            }

            if (KeyboardState.IsKeyDown(Key.ShiftLeft))
            {
                // Camera truck and pedestal movement (blender calls this pan)
                var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

                Location += GetUpVector() * speed * -MouseDelta.Y;
                Location += GetRightVector() * speed * MouseDelta.X;
            }
            else if (KeyboardState.IsKeyDown(Key.AltLeft))
            {
                // Move forward or backwards when holding alt
                var totalDelta = MouseDelta.X + (MouseDelta.Y * -1);
                var speed = AltMovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

                Location += GetForwardVector() * totalDelta * speed;
            }
            else
            {
                // Use the keyboard state to update position
                HandleKeyboardInput(deltaTime);

                // Full width of the screen is a 1 PI (180deg)
                Yaw -= MathF.PI * MouseDelta.X / WindowSize.X;
                Pitch -= MathF.PI / AspectRatio * MouseDelta.Y / WindowSize.Y;
            }

            ClampRotation();

            RecalculateMatrices();
        }

        public float ModifySpeed(float subRange)
        {
            subRange = Math.Clamp(subRange, 0, 1.0f);
            CurrentSpeedModifier = (int)Math.Round(subRange * (SpeedModifiers.Length - 1));
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

        public void HandleInput(MouseState mouseState, KeyboardState keyboardState)
        {
            KeyboardState = keyboardState;

            if (MouseOverRenderArea && (mouseState.LeftButton == ButtonState.Pressed || mouseState.RightButton == ButtonState.Pressed))
            {
                if (!MouseDragging)
                {
                    MouseDragging = true;
                    MousePreviousPosition = new Vector2(mouseState.X, mouseState.Y);
                }

                var mouseNewCoords = new Vector2(mouseState.X, mouseState.Y);

                MouseDelta.X = mouseNewCoords.X - MousePreviousPosition.X;
                MouseDelta.Y = mouseNewCoords.Y - MousePreviousPosition.Y;

                MousePreviousPosition = mouseNewCoords;
            }

            if (!MouseOverRenderArea || !mouseState.IsConnected || (mouseState.LeftButton == ButtonState.Released && mouseState.RightButton == ButtonState.Released))
            {
                MouseDragging = false;
                MouseDelta = default;
            }
        }

        private void HandleKeyboardInput(float deltaTime)
        {
            var speed = MovementSpeed * deltaTime * SpeedModifiers[CurrentSpeedModifier];

            if (KeyboardState.IsKeyDown(Key.W))
            {
                Location += GetForwardVector() * speed;
            }

            if (KeyboardState.IsKeyDown(Key.S))
            {
                Location -= GetForwardVector() * speed;
            }

            if (KeyboardState.IsKeyDown(Key.D))
            {
                Location += GetRightVector() * speed;
            }

            if (KeyboardState.IsKeyDown(Key.A))
            {
                Location -= GetRightVector() * speed;
            }

            if (KeyboardState.IsKeyDown(Key.Z))
            {
                Location += new Vector3(0, 0, -speed);
            }

            if (KeyboardState.IsKeyDown(Key.Q))
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
