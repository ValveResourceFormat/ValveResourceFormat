namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Perspective camera with view and projection matrix management.
    /// </summary>
    public class Camera
    {
        /// <summary>
        /// World-space position of the camera.
        /// </summary>
        public Vector3 Location { get; set; }

        /// <summary>
        /// Vertical rotation angle in radians.
        /// </summary>
        public float Pitch { get; set; }

        /// <summary>
        /// Horizontal rotation angle in radians.
        /// </summary>
        public float Yaw { get; set; }

        /// <summary>
        /// Unit vector pointing in the camera's look direction.
        /// </summary>
        public Vector3 Forward { get; private set; }

        /// <summary>
        /// Unit vector pointing to the camera's right.
        /// </summary>
        public Vector3 Right { get; private set; }

        /// <summary>
        /// Unit vector pointing upward from the camera's perspective.
        /// </summary>
        public Vector3 Up { get; private set; }

        private RendererContext RendererContext;

        /// <summary>
        /// Perspective projection matrix (reverse-Z, infinite far plane).
        /// </summary>
        public Matrix4x4 ProjectionMatrix { get; private set; }

        /// <summary>
        /// World-to-view transform matrix.
        /// </summary>
        public Matrix4x4 CameraViewMatrix { get; private set; }

        /// <summary>
        /// Combined world-to-clip transform matrix.
        /// </summary>
        public Matrix4x4 ViewProjectionMatrix { get; private set; }

        /// <summary>
        /// Frustum derived from the current view-projection matrix, used for culling.
        /// </summary>
        public Frustum ViewFrustum { get; } = new Frustum();

        /// <summary>
        /// Current viewport dimensions in pixels.
        /// </summary>
        public Vector2 WindowSize { get; private set; }

        /// <summary>
        /// Viewport width divided by height.
        /// </summary>
        public float AspectRatio { get; private set; }

        /// <summary>
        /// Initializes a new camera with a default position and 16:9 viewport.
        /// </summary>
        /// <param name="rendererContext">Renderer context used to read field-of-view settings.</param>
        public Camera(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
            Location = Vector3.One;
            SetViewportSize(16, 9);
            LookAt(Vector3.Zero);
        }

        /// <summary>
        /// Recomputes view, projection, and view-projection matrices from the current location, pitch, and yaw.
        /// </summary>
        public void RecalculateMatrices()
        {
            var (location, pitch, yaw) = (Location, Pitch, Yaw);

            RecalculateDirectionVectors();

            CameraViewMatrix = Matrix4x4.CreateLookAt(location, location + Forward, Vector3.UnitZ);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        /// <summary>
        /// Recomputes <see cref="Forward"/>, <see cref="Up"/>, and <see cref="Right"/> vectors from the current pitch and yaw.
        /// </summary>
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

        /// <summary>
        /// Writes camera matrices and direction vectors into the provided view constants struct.
        /// </summary>
        /// <param name="viewConstants">View constants struct to populate.</param>
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

        /// <summary>
        /// Updates the viewport dimensions and rebuilds the projection matrix.
        /// </summary>
        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            CreateProjectionMatrix();
        }

        /// <summary>
        /// Rebuilds <see cref="ProjectionMatrix"/> from the current field of view and aspect ratio.
        /// </summary>
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

        /// <summary>
        /// Copies all transform state from another camera into this one.
        /// </summary>
        /// <param name="fromOther">The camera to copy from.</param>
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

        /// <summary>Sets <see cref="Location"/> without recalculating matrices.</summary>
        public void SetLocation(Vector3 location)
        {
            Location = location;
        }

        /// <summary>Sets <see cref="Location"/>, <see cref="Pitch"/>, and <see cref="Yaw"/> without recalculating matrices.</summary>
        public void SetLocationPitchYaw(Vector3 location, float pitch, float yaw)
        {
            Location = location;
            Pitch = pitch;
            Yaw = yaw;
        }

        /// <summary>
        /// Orients the camera to face the given world-space target.
        /// </summary>
        public void LookAt(Vector3 target)
        {
            var dir = Vector3.Normalize(target - Location);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);

            ClampRotation();
        }


        /// <summary>
        /// Positions the camera so the specified bounding box fills the view.
        /// </summary>
        /// <param name="objectPosition">Center of the object to frame.</param>
        /// <param name="width">Width of the object's bounding box.</param>
        /// <param name="height">Height of the object's bounding box.</param>
        /// <param name="depth">Depth of the object's bounding box.</param>
        public void FrameObject(Vector3 objectPosition, float width, float height, float depth)
        {
            if (width == 0 && height == 0 && depth == 0)
            {
                return;
            }

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

        /// <summary>
        /// Positions the camera at the given yaw/pitch angle so the bounding box fills the view.
        /// </summary>
        /// <param name="objectPosition">Center of the object to frame.</param>
        /// <param name="width">Width of the object's bounding box.</param>
        /// <param name="height">Height of the object's bounding box.</param>
        /// <param name="depth">Depth of the object's bounding box.</param>
        /// <param name="yaw">Horizontal angle in radians.</param>
        /// <param name="pitch">Vertical angle in radians.</param>
        public void FrameObjectFromAngle(Vector3 objectPosition, float width, float height, float depth,
            float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
            ClampRotation();
            RecalculateDirectionVectors();

            FrameObject(objectPosition, width, height, depth);
        }

        /// <summary>
        /// Sets the camera position and orientation from a transform matrix.
        /// </summary>
        /// <param name="matrix">Transform matrix whose translation and first row determine position and direction.</param>
        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = MathF.Atan2(dir.Y, dir.X);
            Pitch = MathF.Asin(dir.Z);
        }

        /// <summary>
        /// Clamps <see cref="Pitch"/> to prevent the camera from flipping upside-down.
        /// </summary>
        public void ClampRotation()
        {
            const float PITCH_LIMIT = 89.5f * MathF.PI / 180f;
            Pitch = Math.Clamp(Pitch, -PITCH_LIMIT, PITCH_LIMIT);
        }

        private float GetFOV()
        {
            return float.DegreesToRadians(RendererContext.FieldOfView);
        }
    }
}
