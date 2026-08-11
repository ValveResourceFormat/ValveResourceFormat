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
        /// Vertical rotation angle in radians, positive looking down, as in a Source QAngle.
        /// </summary>
        public float Pitch { get; set; }

        /// <summary>
        /// Horizontal rotation angle in radians.
        /// </summary>
        public float Yaw { get; set; }

        /// <summary>
        /// Clockwise rotation angle around the camera's forward axis in radians.
        /// </summary>
        public float Roll { get; set; }

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

        /// <summary>
        /// Horizontal field of view in degrees.
        /// Converted to the vertical FOV actually used to build <see cref="ProjectionMatrix"/> by <see cref="GetFOV"/>.
        /// </summary>
        public float FieldOfView { get; set; }

        /// <summary>
        /// Perspective projection matrix (reverse-Z; the far plane is infinite unless <see cref="FarPlane"/> is set).
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
        /// Initializes a new camera with a default position, 16:9 viewport, and the given field of view.
        /// </summary>
        /// <param name="fieldOfView">Initial field of view in degrees (horizontal at 4:3, see <see cref="FieldOfView"/>).</param>
        public Camera(float fieldOfView = 90f)
        {
            FieldOfView = fieldOfView;
            Location = Vector3.One;
            SetViewportSize(16, 9);
            LookAt(Vector3.Zero);
        }

        /// <summary>
        /// Recomputes the view and view-projection matrices (and updates the view frustum) from the current location and orientation. The projection matrix is not rebuilt here (see <see cref="CreateProjectionMatrix"/>).
        /// </summary>
        public void RecalculateMatrices()
        {
            var location = Location;

            RecalculateDirectionVectors();

            CameraViewMatrix = Matrix4x4.CreateLookAt(location, location + Forward, Up);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        /// <summary>
        /// Recomputes <see cref="Forward"/>, <see cref="Up"/>, and <see cref="Right"/> vectors from the current pitch, yaw, and roll.
        /// </summary>
        public void RecalculateDirectionVectors()
        {
            var (yawSin, yawCos) = MathF.SinCos(Yaw);
            var (pitchSin, pitchCos) = MathF.SinCos(Pitch);

            Forward = new Vector3(yawCos * pitchCos, yawSin * pitchCos, -pitchSin);
            Up = new Vector3(yawCos * pitchSin, yawSin * pitchSin, pitchCos);

            // Cross(Forward, Up) worked through by hand: it comes out as yaw shifted a quarter turn, with
            // no pitch term at all, so there is still a right to point along when looking straight down.
            Right = new Vector3(yawSin, -yawCos, 0);

            if (Roll != 0f)
            {
                // Rolling turns Up and Right about Forward, which both are perpendicular to, so the
                // rotation is just the pair leaning into each other
                var (rollSin, rollCos) = MathF.SinCos(Roll);
                var rolledUp = (Up * rollCos) + (Right * rollSin);

                Right = (Right * rollCos) - (Up * rollSin);
                Up = rolledUp;
            }
        }

        /// <summary>
        /// Writes camera matrices and direction vectors into the provided view constants object.
        /// </summary>
        /// <param name="viewConstants">View constants object to populate.</param>
        public void SetViewConstants(Buffers.ViewConstants viewConstants)
        {
            viewConstants.WorldToProjection = ViewProjectionMatrix;
            viewConstants.WorldToView = CameraViewMatrix;
            viewConstants.ViewToProjection = ProjectionMatrix;
            viewConstants.CameraPosition = Location;

            if (!Matrix4x4.Invert(ProjectionMatrix, out viewConstants.ProjectionToWorld))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            viewConstants.InvProjRow3 = new Vector4(
                viewConstants.ProjectionToWorld.M14,
                viewConstants.ProjectionToWorld.M24,
                viewConstants.ProjectionToWorld.M34,
                viewConstants.ProjectionToWorld.M44
            );

            viewConstants.CameraDirWs = Forward;
            viewConstants.CameraUpDirWs = Up;

            // todo: these change per scene, move to the other buffer
            viewConstants.ViewportMinZ = Renderer.DepthRange.Scene.Near;
            viewConstants.ViewportMaxZ = Renderer.DepthRange.Scene.Far;
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
        /// Distance from the eye to the near plane. Also where the light cull pass clips light volumes and
        /// where its depth slices start. Call <see cref="CreateProjectionMatrix"/> after changing it.
        /// </summary>
        public float NearPlane { get; set; } = 1.0f;

        /// <summary>
        /// Distance from the eye to the far plane, or <see cref="float.PositiveInfinity"/> for none, the
        /// default. Call <see cref="CreateProjectionMatrix"/> after changing it.
        /// </summary>
        public float FarPlane { get; set; } = float.PositiveInfinity;

        /// <summary>
        /// Rebuilds <see cref="ProjectionMatrix"/> from the current field of view and aspect ratio.
        /// </summary>
        public void CreateProjectionMatrix()
        {
            ProjectionMatrix = CreatePerspectiveFieldOfView_ReverseZ(GetFOV(), AspectRatio, NearPlane, FarPlane);
        }

        /// <summary>
        /// Maps a pixel of this camera's view to the one the same world ray lands on in
        /// <paramref name="target"/>'s view, as an xy scale and a zw bias in pixels. This is what lets a
        /// pass drawn at a different field of view find its light cull tile from gl_FragCoord alone.
        /// </summary>
        /// <remarks>
        /// Exact while the two cameras differ only in projection, which is what the viewmodel is. Both
        /// turn a view ray into ndc by the same shape of expression, <c>ndc = scale * (x / -z)</c>, so
        /// eliminating the ray leaves an affine map with no dependence on distance along it.
        /// </remarks>
        /// <param name="target">The camera whose screen the result maps into.</param>
        /// <param name="viewportSize">Pixel dimensions both cameras draw into.</param>
        public Vector4 GetPixelRemapTo(Camera target, Vector2 viewportSize)
        {
            var scale = new Vector2(
                ProjectionMatrix.M11 != 0f ? target.ProjectionMatrix.M11 / ProjectionMatrix.M11 : 1f,
                ProjectionMatrix.M22 != 0f ? target.ProjectionMatrix.M22 / ProjectionMatrix.M22 : 1f);

            var bias = viewportSize * 0.5f * (Vector2.One - scale);

            return new Vector4(scale.X, scale.Y, bias.X, bias.Y);
        }

        /// <inheritdoc cref="Matrix4x4.CreatePerspectiveFieldOfView"/>
        /// <remarks>
        /// Reverse-Z: the near plane maps to ndc z 1 and the far plane to 0. An infinite far plane is the
        /// limit of the finite case, but the finite expressions evaluate to NaN there, so it branches.
        /// </remarks>
        private static Matrix4x4 CreatePerspectiveFieldOfView_ReverseZ(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance)
        {
            var height = 1.0f / MathF.Tan(fieldOfView * 0.5f);
            var width = height / aspectRatio;

            var m33 = 0.0f;
            var m43 = nearPlaneDistance;

            if (float.IsFinite(farPlaneDistance))
            {
                var range = farPlaneDistance - nearPlaneDistance;

                m33 = nearPlaneDistance / range;
                m43 = nearPlaneDistance * farPlaneDistance / range;
            }

            return new Matrix4x4
            {
                M11 = width,
                M22 = height,
                M33 = m33,
                M34 = -1.0f,
                M43 = m43
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
            NearPlane = fromOther.NearPlane;
            FarPlane = fromOther.FarPlane;
            Location = fromOther.Location;
            Pitch = fromOther.Pitch;
            Yaw = fromOther.Yaw;
            Roll = fromOther.Roll;
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
        /// Adopts the orientation of a Source QAngle, as read from an entity's "angles" keyvalue.
        /// </summary>
        /// <remarks>
        /// Only the degrees to radians conversion; the camera holds these the same way round as the engine
        /// does. All three are adopted, so angles that carry no roll clear whatever roll was there rather
        /// than leaving it to show through. Not clamped, so an entity may point the camera anywhere.
        /// </remarks>
        /// <param name="anglesDegrees">The QAngle, as (pitch, yaw, roll) in degrees.</param>
        public void SetFromQAngle(Vector3 anglesDegrees)
        {
            Pitch = float.DegreesToRadians(anglesDegrees.X);
            Yaw = float.DegreesToRadians(anglesDegrees.Y);
            Roll = float.DegreesToRadians(anglesDegrees.Z);
        }

        /// <summary>
        /// The camera's orientation as a Source QAngle, the inverse of <see cref="SetFromQAngle"/>.
        /// </summary>
        /// <returns>The QAngle, as (pitch, yaw, roll) in degrees.</returns>
        public Vector3 GetQAngle()
            => new(float.RadiansToDegrees(Pitch), float.RadiansToDegrees(Yaw), float.RadiansToDegrees(Roll));

        /// <summary>
        /// Orients the camera to face the given world-space target.
        /// </summary>
        public void LookAt(Vector3 target)
        {
            // Normalized because the helper decides "straight up or down" on absolute length, which is
            // right for a world direction but would make a target a thousandth of a unit away read as one
            var direction = target - Location;

            // A direction carries no roll, so adopting these angles levels the camera too
            SetFromQAngle(EntityTransformHelper.ForwardDirectionToEulerAngles(
                direction == Vector3.Zero ? direction : Vector3.Normalize(direction)));

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

            // Previous operation might have corrupted our angles
            if (float.IsNaN(Yaw) || float.IsNaN(Pitch))
            {
                Yaw = 0;
                Pitch = 0;
            }

            RecalculateDirectionVectors();

            var fov = GetFOV();
            var halfFovVertical = fov * 0.5f;
            var halfFovHorizontal = MathF.Atan(MathF.Tan(halfFovVertical) * AspectRatio);

            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var halfDepth = depth * 0.5f;

            // this calculates the apparent size in screen space by projecting onto camera axis
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
        /// <param name="pitch">Vertical angle in radians, positive looking down.</param>
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

            // Forward is the matrix's first row. The helper is scale invariant, which matters because an
            // entity's "scales" is baked in here, but it cannot answer for a row that is entirely zero.
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);

            if (dir == Vector3.Zero)
            {
                return;
            }

            SetFromQAngle(EntityTransformHelper.ForwardDirectionToEulerAngles(dir));
        }

        /// <summary>
        /// Clamps <see cref="Pitch"/> to prevent the camera from flipping upside-down.
        /// </summary>
        public void ClampRotation()
        {
            // Straight down is as far as it goes, and it is reachable: the direction vectors have no
            // singularity there, so the limit is where looking would tip over rather than where the maths
            // would give out.
            Pitch = Math.Clamp(Pitch, -MathF.PI / 2f, MathF.PI / 2f);

            // Mouse look accumulates yaw without bound, and sin and cos of a few million radians are not
            // the ones asked for, so keep it to one turn
            Yaw = MathF.IEEERemainder(Yaw, MathF.Tau);
        }

        /// <summary>
        /// Returns the vertical FOV in radians used to build <see cref="ProjectionMatrix"/>, converted from
        /// <see cref="FieldOfView"/> via <see cref="Calculate4By3Fov"/>.
        /// </summary>
        public float GetFOV()
        {
            return Calculate4By3Fov(FieldOfView);
        }

        /// <summary>
        /// Converts a horizontal field of view at a 4:3 aspect ratio to the equivalent vertical field of view in radians.
        /// The result is used directly as the vertical FOV in <see cref="CreatePerspectiveFieldOfView_ReverseZ"/> regardless
        /// of the actual aspect ratio, so a wider screen sees more to the sides rather than less above and
        /// below (Hor+ scaling). The engine reaches the same place by scaling the horizontal field of view
        /// by the aspect ratio and then dividing it back out again.
        /// </summary>
        /// <param name="horizontalDegreesAt4By3">Horizontal field of view in degrees at a 4:3 aspect ratio.</param>
        public static float Calculate4By3Fov(float horizontalDegreesAt4By3)
        {
            return 2f * MathF.Atan(MathF.Tan(float.DegreesToRadians(horizontalDegreesAt4By3) * 0.5f) / (4f / 3f));
        }
    }
}
