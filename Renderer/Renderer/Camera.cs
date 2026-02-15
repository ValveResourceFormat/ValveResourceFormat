namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Perspective camera with view and projection matrix management.
    /// </summary>
    public class Camera
    {
        public Vector3 Location { get; set; }
        public float Pitch { get; set; }
        public float Yaw { get; set; }

        public Vector3 Forward { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 Up { get; private set; }

        private RendererContext RendererContext;
        public Matrix4x4 ProjectionMatrix { get; private set; }
        public Matrix4x4 CameraViewMatrix { get; private set; }
        public Matrix4x4 ViewProjectionMatrix { get; private set; }
        public Frustum ViewFrustum { get; } = new Frustum();

        public Vector2 WindowSize { get; private set; }
        public float AspectRatio { get; private set; }

        public Camera(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
            Location = Vector3.One;
            SetViewportSize(16, 9);
            LookAt(Vector3.Zero);
        }

        public void RecalculateMatrices()
        {
            var (location, pitch, yaw) = (Location, Pitch, Yaw);

            RecalculateDirectionVectors();

            CameraViewMatrix = Matrix4x4.CreateLookAt(location, location + Forward, Vector3.UnitZ);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        public void RecalculateDirectionVectors()
        {
            var (yawSin, yawCos) = MathF.SinCos(Yaw);
            var (pitchSin, pitchCos) = MathF.SinCos(Pitch);

            Forward = new Vector3(yawCos * pitchCos, yawSin * pitchCos, pitchSin);
            Up = new Vector3(yawCos * pitchSin, yawSin * pitchSin, pitchCos);

            const float PiOver2 = MathF.PI / 2f;
            var (piOver2Sin, piOver2Cos) = MathF.SinCos(Yaw - PiOver2);

            Right = new Vector3(piOver2Cos, piOver2Sin, 0);
            // Right = Vector3.Cross(Forward, Up);
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

            viewConstants.CameraDirWs = Forward;
            viewConstants.CameraUpDirWs = Up;

            // todo: these change per scene, move to the other buffer
            viewConstants.ViewportMinZ = 0.05f;
            viewConstants.ViewportMaxZ = 1.0f;
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            CreateProjectionMatrix();
        }

        public void CreateProjectionMatrix()
        {
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


        public void FrameObject(Vector3 objectPosition, float width, float height, float depth)
        {
            var fov = GetFOV();
            var halfFovVertical = fov * 0.5f;
            var halfFovHorizontal = MathF.Atan(MathF.Tan(halfFovVertical) * AspectRatio);

            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var halfDepth = depth * 0.5f;

            // this calculate the apparent size in screen space by projecting onto camera axis
            var maxHorizontalExtent = 0f;
            var maxVerticalExtent = 0f;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) != 0 ? halfWidth : -halfWidth,
                    (i & 2) != 0 ? halfHeight : -halfHeight,
                    (i & 4) != 0 ? halfDepth : -halfDepth
                );

                var horizontalDist = MathF.Abs(Vector3.Dot(corner, Right));
                var verticalDist = MathF.Abs(Vector3.Dot(corner, Up));

                maxHorizontalExtent = MathF.Max(maxHorizontalExtent, horizontalDist);
                maxVerticalExtent = MathF.Max(maxVerticalExtent, verticalDist);
            }

            var distanceForVerticalFov = maxVerticalExtent / MathF.Tan(halfFovVertical);
            var distanceForHorizontalFov = maxHorizontalExtent / MathF.Tan(halfFovHorizontal);

            var distance = MathF.Max(distanceForVerticalFov, distanceForHorizontalFov);

            Location = objectPosition - Forward * distance;

            LookAt(objectPosition);
        }

        public void FrameObjectFromAngle(Vector3 objectPosition, float width, float height, float depth,
            float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
            ClampRotation();
            RecalculateDirectionVectors();

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

        // Prevent camera from going upside-down
        public void ClampRotation()
        {
            const float PITCH_LIMIT = 89.5f * MathF.PI / 180f;
            Pitch = Math.Clamp(Pitch, -PITCH_LIMIT, PITCH_LIMIT);
        }

        private float GetFOV()
        {
            return MathUtils.ToRadians(RendererContext.FieldOfView);
        }
    }
}
