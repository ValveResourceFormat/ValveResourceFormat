using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace GUI.Types.Renderer
{
    internal class Camera
    {
        private const float CAMERASPEED = 300f;
        private const float FOV = MathHelper.PiOver4;

        private readonly string Name;

        private Vector2 WindowSize;
        private float AspectRatio;

        public Matrix4 ProjectionMatrix;
        public Matrix4 CameraViewMatrix;

        public bool MouseOverRenderArea { get; set; }
        private bool MouseDragging;

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;

        public Vector3 Location;
        private float Pitch;
        public float Yaw;

        private KeyboardState KeyboardState;

        public Camera(int viewportWidth, int viewportHeight, Vector3 minBounds, Vector3 maxBounds, string name = "Default")
        {
            SetViewportSize(viewportWidth, viewportHeight);

            Location.Y = (maxBounds.X + minBounds.X) / 2;
            Location.X = maxBounds.Y + 30.0f;
            Location.Z = maxBounds.Z + 30.0f;
            var quaternion = CameraViewMatrix.ExtractRotation(true);
            Pitch = quaternion.Y;
            Yaw = quaternion.Z;
            // TODO: needs fixing
            Yaw = 3f;
            Pitch = -0.9f;

            Name = name;
        }

        public Camera(int viewportWidth, int viewportHeight, Matrix4 cameraViewMatrix, string name = "Default")
        {
            SetViewportSize(viewportWidth, viewportHeight);

            Location = cameraViewMatrix.ExtractTranslation();
            CameraViewMatrix = cameraViewMatrix;

            //TODO: Someone figure out what this section is meant to be. (tree_game is a good test)
            var quaternion = CameraViewMatrix.ExtractRotation(false);
            Pitch = quaternion.Y;
            Yaw = quaternion.Z;

            Name = name;
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            var aspectRatio = viewportWidth / (float)viewportHeight;
            ProjectionMatrix = Matrix4.CreatePerspectiveFieldOfView(FOV, aspectRatio, 1.0f, 40000.0f);

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

            // Full width of the screen is a 1 * FOV rotation
            Yaw -= FOV * AspectRatio * MouseDelta.X / WindowSize.X;
            Pitch -= FOV * MouseDelta.Y / WindowSize.Y;

            ClampRotation();

            var lookatPoint = new Vector3((float)Math.Cos(Yaw), (float)Math.Sin(Yaw), (float)Pitch);
            CameraViewMatrix = Matrix4.LookAt(Location, Location + lookatPoint, Vector3.UnitZ);
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
                Location += new Vector3((float)Math.Cos(Yaw), (float)Math.Sin(Yaw), Pitch) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.S))
            {
                Location -= new Vector3((float)Math.Cos(Yaw), (float)Math.Sin(Yaw), Pitch) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.D))
            {
                Location -= new Vector3((float)Math.Cos(Yaw + MathHelper.PiOver2), (float)Math.Sin(Yaw + MathHelper.PiOver2), 0) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.A))
            {
                Location += new Vector3((float)Math.Cos(Yaw + MathHelper.PiOver2), (float)Math.Sin(Yaw + MathHelper.PiOver2), 0) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.Z))
            {
                Location.Z -= speed;
            }

            if (KeyboardState.IsKeyDown(Key.Q))
            {
                Location.Z += speed;
            }
        }

        private void ClampRotation()
        {
            if (Pitch >= Math.PI)
            {
                Pitch = (float)Math.PI;
            }
            else if (Pitch <= -Math.PI)
            {
                Pitch = (float)-Math.PI;
            }

            if (Yaw >= MathHelper.TwoPi)
            {
                Yaw -= MathHelper.TwoPi;
            }
            else if (Yaw <= -MathHelper.TwoPi)
            {
                Yaw += MathHelper.TwoPi;
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
