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
        private const float FOV = OpenTK.MathHelper.PiOver4;

        private readonly float[] SpeedModifiers = new float[]
        {
            0.1f,
            0.5f,
            1.0f,
            2.0f,
            5.0f,
            10.0f,
        };
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

        private bool MouseDragging;

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;

        private KeyboardState KeyboardState;

        public Camera()
        {
            Location = new Vector3(1);
            LookAt(new Vector3(0));
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
            var yawSin = Math.Sin(Yaw);
            var yawCos = Math.Cos(Yaw);
            var pitchSin = Math.Sin(Pitch);
            var pitchCos = Math.Cos(Pitch);
            return new Vector3((float)(yawCos * pitchCos), (float)(yawSin * pitchCos), (float)pitchSin);
        }

        private Vector3 GetUpVector()
        {
            var yawSin = Math.Sin(Yaw);
            var yawCos = Math.Cos(Yaw);
            var pitchSin = Math.Sin(Pitch);
            var pitchCos = Math.Cos(Pitch);
            return new Vector3((float)(yawCos * pitchSin), (float)(yawSin * pitchSin), (float)pitchCos);
        }

        private Vector3 GetRightVector()
        {
            return new Vector3((float)Math.Cos(Yaw - OpenTK.MathHelper.PiOver2), (float)Math.Sin(Yaw - OpenTK.MathHelper.PiOver2), 0);
        }

        public void SetPerViewUniforms(Shader shader)
        {
            var worldToProjection = ProjectionMatrix.ToOpenTK();
            var worldToView = CameraViewMatrix.ToOpenTK();
            var viewToProjection = ViewProjectionMatrix.ToOpenTK();

            GL.UniformMatrix4(shader.GetUniformLocation("g_matWorldToProjection"), false, ref worldToProjection);
            GL.UniformMatrix4(shader.GetUniformLocation("g_matWorldToView"), false, ref worldToView);
            GL.UniformMatrix4(shader.GetUniformLocation("g_matViewToProjection"), false, ref viewToProjection);
            GL.Uniform3(shader.GetUniformLocation("g_vCameraPositionWs"), Location.ToOpenTK());
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FOV, AspectRatio, 1.0f, 20000.0f);

            RecalculateMatrices();

            // setup viewport
            GL.Viewport(0, 0, viewportWidth, viewportHeight);

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
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FOV, AspectRatio, 10f * Scale, 20000.0f * Scale);
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
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

            ClampRotation();
            RecalculateMatrices();
        }

        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

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
                Yaw -= (float)Math.PI * MouseDelta.X / WindowSize.X;
                Pitch -= (float)Math.PI / AspectRatio * MouseDelta.Y / WindowSize.Y;
            }

            ClampRotation();

            RecalculateMatrices();
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
            const float PITCH_LIMIT = 89.5f * ((float)Math.PI / 180f);

            if (Pitch >= PITCH_LIMIT)
            {
                Pitch = PITCH_LIMIT;
            }
            else if (Pitch <= -PITCH_LIMIT)
            {
                Pitch = -PITCH_LIMIT;
            }
        }
    }
}
