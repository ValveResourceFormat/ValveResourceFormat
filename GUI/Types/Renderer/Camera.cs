using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace GUI.Types.Renderer
{
    internal class Camera
    {
        private const float CAMERASPEED = 300f; // Per second
        private const float FOV = MathHelper.PiOver4;

        public string Name { get; protected set; }

        public Vector3 Location { get; private set; }
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }

        public Matrix4 ProjectionMatrix { get; private set; }
        public Matrix4 CameraViewMatrix { get; private set; }

        // Set from outside this class by forms code
        public bool MouseOverRenderArea { get; set; }

        public Vector2 WindowSize { get; set; }
        private float AspectRatio;

        private bool MouseDragging;

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;

        private KeyboardState KeyboardState;

        public Camera(Vector3 minBounds, Vector3 maxBounds, string name = "Default")
        {
            // Calculate center of bounding box
            var bboxCenter = (minBounds + maxBounds) * 0.5f;

            // Set initial position based on the bounding box
            Location = new Vector3(maxBounds.Z, 0, maxBounds.Z) * 1.5f;

            // Calculate yaw and pitch
            var dir = (bboxCenter - Location).Normalized();
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

            // Build camera view matrix
            CameraViewMatrix = Matrix4.LookAt(Location, bboxCenter, Vector3.UnitZ);

            Name = name;
        }

        public Camera(Matrix4 transformationMatrix, string name = "Default")
        {
            Location = transformationMatrix.ExtractTranslation();

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = transformationMatrix.Row0.Xyz;
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

            // Build camera view matrix
            CameraViewMatrix = Matrix4.LookAt(Location, Location + dir, Vector3.UnitZ);

            Name = name;
        }

        // Make a copy of another camera
        public Camera(Camera original)
        {
            Location = original.Location;
            Pitch = original.Pitch;
            Yaw = original.Yaw;
            Name = original.Name;

            // Build camera view matrix
            CameraViewMatrix = Matrix4.LookAt(Location, Location + GetForwardVector(), Vector3.UnitZ);
        }

        // Calculate forward vector from pitch and yaw
        private Vector3 GetForwardVector()
        {
            return new Vector3((float)(Math.Cos(Yaw) * Math.Cos(Pitch)), (float)(Math.Sin(Yaw) * Math.Cos(Pitch)), (float)Math.Sin(Pitch));
        }

        private Vector3 GetRightVector()
        {
            return new Vector3((float)Math.Cos(Yaw - MathHelper.PiOver2), (float)Math.Sin(Yaw - MathHelper.PiOver2), 0);
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            ProjectionMatrix = Matrix4.CreatePerspectiveFieldOfView(FOV, AspectRatio, 1.0f, 40000.0f);

            // setup viewport
            GL.Viewport(0, 0, viewportWidth, viewportHeight);
        }

        public void Tick(float deltaTime)
        {
            if (!MouseOverRenderArea)
            {
                return;
            }

            // Use the keyboard state to update position
            HandleKeyboardInput(deltaTime);

            // Full width of the screen is a 1 PI (180deg)
            Yaw -= (float)Math.PI * MouseDelta.X / WindowSize.X;
            Pitch -= ((float)Math.PI / AspectRatio) * MouseDelta.Y / WindowSize.Y;

            ClampRotation();

            CameraViewMatrix = Matrix4.LookAt(Location, Location + GetForwardVector(), Vector3.UnitZ);
        }

        public void HandleInput(MouseState mouseState, KeyboardState keyboardState)
        {
            KeyboardState = keyboardState;

            if (MouseOverRenderArea && mouseState.LeftButton == ButtonState.Pressed)
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

            if (!MouseOverRenderArea || mouseState.LeftButton == ButtonState.Released)
            {
                MouseDragging = false;
                MouseDelta = default(Vector2);
            }
        }

        private void HandleKeyboardInput(float deltaTime)
        {
            var speed = CAMERASPEED * deltaTime;

            // Double speed if shift is pressed
            if (KeyboardState.IsKeyDown(Key.ShiftLeft))
            {
                speed *= 2;
            }

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
            if (Pitch >= MathHelper.PiOver2)
            {
                Pitch = MathHelper.PiOver2 - 0.001f;
            }
            else if (Pitch <= -MathHelper.PiOver2)
            {
                Pitch = -MathHelper.PiOver2 + 0.001f;
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
