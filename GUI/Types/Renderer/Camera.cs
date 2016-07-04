using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace GUI.Types.Renderer
{
    internal class Camera
    {
        private readonly Stopwatch PreciseTimer;

        public Matrix4 ProjectionMatrix;
        public Matrix4 CameraViewMatrix;

        public bool MouseOverRenderArea { get; set; }
        private bool MouseDragging;

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;
        private Vector2 MouseSpeed = new Vector2(0f, 0f);

        public Vector3 Location;
        private double Pitch;
        public double Yaw;

        private KeyboardState KeyboardState;

        private readonly string Name;

        public Camera(int viewportWidth, int viewportHeight, Vector3 minBounds, Vector3 maxBounds, string name = "Default")
        {
            PreciseTimer = new Stopwatch();

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
            PreciseTimer = new Stopwatch();

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
            var aspectRatio = viewportWidth / (float)viewportHeight;
            ProjectionMatrix = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, aspectRatio, 1.0f, 40000.0f);

            // setup projection
            GL.Viewport(0, 0, viewportWidth, viewportHeight);
        }

        private double accumulator;
        private int idleCounter;

        public void Tick(ref string fpsString)
        {
            if (!MouseOverRenderArea)
            {
                return;
            }

            var deltaTime = GetElapsedTime();
            var speed = KeyboardState.IsKeyDown(Key.ShiftLeft) ? 0.6f : 0.1f;
            speed *= deltaTime;

            idleCounter++;
            accumulator += deltaTime;
            if (accumulator > 1000)
            {
                //Console.WriteLine("{0} FPS, {1}", idleCounter, accumulator / idleCounter);
                fpsString = $"FPS: {idleCounter}";
                accumulator -= 1000;
                idleCounter = 0; // don't forget to reset the counter!
            }

            if (KeyboardState.IsKeyDown(Key.W))
            {
                Location.X += (float)Math.Cos(Yaw) * speed;
                Location.Y += (float)Math.Sin(Yaw) * speed;
                Location.Z += (float)Pitch * speed;
            }

            if (KeyboardState.IsKeyDown(Key.S))
            {
                Location.X -= (float)Math.Cos(Yaw) * speed;
                Location.Y -= (float)Math.Sin(Yaw) * speed;
                Location.Z -= (float)Pitch * speed;
            }

            if (KeyboardState.IsKeyDown(Key.D))
            {
                Location.X -= (float)Math.Cos(Yaw + MathHelper.PiOver2) * speed;
                Location.Y -= (float)Math.Sin(Yaw + MathHelper.PiOver2) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.A))
            {
                Location.X += (float)Math.Cos(Yaw + MathHelper.PiOver2) * speed;
                Location.Y += (float)Math.Sin(Yaw + MathHelper.PiOver2) * speed;
            }

            if (KeyboardState.IsKeyDown(Key.Z))
            {
                Location.Z -= speed;
            }

            if (KeyboardState.IsKeyDown(Key.Q))
            {
                Location.Z += speed;
            }

            // TODO: Scale all this by detaltime properly, fails awfully at 5000FPS (yes, really)
            MouseSpeed.X *= 0.4f;
            MouseSpeed.Y *= 0.4f;
            MouseSpeed.X -= MouseDelta.X / (10000f / deltaTime); // TODO: wtf fix this
            MouseSpeed.Y -= MouseDelta.Y / (10000f / deltaTime); // TODO: wtf fix this
            MouseDelta.X = 0f;
            MouseDelta.Y = 0f;

            Yaw += MouseSpeed.X;
            Pitch += MouseSpeed.Y;

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
            }
        }

        private void ClampRotation()
        {
            if (Pitch >= Math.PI)
            {
                Pitch = Math.PI;
            }
            else if (Pitch <= -Math.PI)
            {
                Pitch = -Math.PI;
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

        private float GetElapsedTime()
        {
            var timeslice = PreciseTimer.Elapsed.TotalMilliseconds;

            PreciseTimer.Reset();
            PreciseTimer.Start();

            return (float)timeslice;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
