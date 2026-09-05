using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes skeletal bone hierarchy, animation poses and model attachments.
    /// </summary>
    public class SkeletonSceneNode : SceneNode
    {
        /// <summary>Gets or sets whether the bone hierarchy is drawn.</summary>
        public bool ShowBones { get; set; }

        /// <summary>Gets or sets whether the attachment visualization is drawn.</summary>
        public bool ShowAttachments { get; set; }

        /// <summary>Whether the node has anything to draw.</summary>
        public bool Enabled => ShowBones || ShowAttachments;

        /// <summary>Names of the attachments to draw when <see cref="ShowAttachments"/> is set.</summary>
        public HashSet<string> SelectedAttachments { get; set; } = [];

        // Live pose buffer of whatever animates the skeleton, held by reference.
        readonly Matrix4x4[] pose;
        readonly Skeleton skeleton;
        readonly IReadOnlyDictionary<string, Attachment> attachments;
        readonly LineBuffer lineBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SkeletonSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="pose">The world-space bone pose to visualize, indexed by bone index.</param>
        /// <param name="skeleton">The skeleton definition containing bone hierarchy.</param>
        /// <param name="attachments">Model attachments that can be visualized, or null.</param>
        public SkeletonSceneNode(Scene scene, Matrix4x4[] pose, Skeleton skeleton, IReadOnlyDictionary<string, Attachment>? attachments = null)
            : base(scene)
        {
            this.pose = pose;
            this.skeleton = skeleton;
            this.attachments = attachments ?? new Dictionary<string, Attachment>();

            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(SkeletonSceneNode));
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var vertices = new List<SimpleVertex>();

            if (ShowBones)
            {
                foreach (var root in skeleton.Roots)
                {
                    DrawSkeletonRecursive(root, vertices, context.Camera, context.TextRenderer);
                }
            }

            if (ShowAttachments)
            {
                foreach (var attachmentName in SelectedAttachments)
                {
                    if (attachments.TryGetValue(attachmentName, out var attachment))
                    {
                        DrawAttachment(attachment, vertices, context.Camera, context.TextRenderer);
                    }
                }
            }

            AABB bounds = default;
            var first = true;

            foreach (var vertex in vertices)
            {
                var vertexBounds = new AABB(vertex.Position, 10);

                if (first)
                {
                    bounds = vertexBounds;
                    first = false;
                    continue;
                }

                bounds = bounds.Union(vertexBounds);
            }

            LocalBoundingBox = bounds;

            lineBuffer.Upload(vertices);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer.Delete();
        }

        private float GetSizeCap()
        {
            // todo: bounding box should be from current frame vertices
            var sizeCap = LocalBoundingBox.Size.Length();
            return sizeCap < 1f ? 100f : sizeCap;
        }

        private void DrawLabel(Vector3 localPosition, string text, Color32 color, float sizeCap, Camera camera, TextRenderer textRenderer, out float distance)
        {
            var worldPosition = Vector3.Transform(localPosition, Transform);
            distance = Vector3.Distance(camera.Location, worldPosition);
            var distanceFade = distance > sizeCap ? sizeCap / distance : 1f;

            textRenderer.AddTextBillboard(worldPosition, new TextRenderer.TextRenderRequest
            {
                Scale = 10f * distanceFade,
                Text = text,
                Color = color,
            }, camera);
        }

        // Local axes, normalized to strip any scale from the matrix.
        // Sized proportionally to camera distance so they stay constant on screen.
        private static void DrawAxes(List<SimpleVertex> vertices, in Matrix4x4 matrix, float distance, float sizeCap)
        {
            var origin = matrix.Translation;
            var axisLength = 0.04f * MathF.Min(distance, sizeCap);

            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(matrix.M11, matrix.M12, matrix.M13)) * axisLength, new(1.0f, 0.2f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(matrix.M21, matrix.M22, matrix.M23)) * axisLength, new(0.2f, 0.8f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33)) * axisLength, new(0.2f, 0.2f, 1.0f, 1.0f));
        }

        private void DrawAttachment(Attachment attachment, List<SimpleVertex> vertices, Camera camera, TextRenderer textRenderer)
        {
            var attachmentTransform = ModelSceneNode.GetAttachmentLocalTransform(attachment, skeleton, pose);
            var attachmentPosition = attachmentTransform.Translation;

            var sizeCap = GetSizeCap();
            DrawLabel(attachmentPosition, attachment.Name, new Color32(1.0f, 0.9f, 0.7f, 1.0f), sizeCap, camera, textRenderer, out var distance);

            foreach (var influence in attachment)
            {
                var boneIndex = skeleton.GetBoneIndex(influence.Name);
                if (boneIndex == -1)
                {
                    continue;
                }

                var alpha = Math.Clamp(influence.Weight, 0.15f, 1.0f);
                ShapeSceneNode.AddLine(vertices, attachmentPosition, pose[boneIndex].Translation, new Color32(1.0f, 1.0f, 1.0f, alpha));
            }

            DrawAxes(vertices, attachmentTransform, distance, sizeCap);
        }

        private void DrawSkeletonRecursive(Bone bone, List<SimpleVertex> vertices, Camera camera, TextRenderer textRenderer)
        {
            var boneMatrix = pose[bone.Index];
            var sizeCap = GetSizeCap();

            var color = (bone.Parent, bone.Children.Count) switch
            {
                (null, _) => new Color32(1.0f, 0.8f, 0.8f, 1.0f),
                (_, 0) => new Color32(0.8f, 1.0f, 0.8f, 1.0f),
                _ => Color32.White,
            };

            DrawLabel(boneMatrix.Translation, bone.Name, color, sizeCap, camera, textRenderer, out var distance);

            if (bone.Parent != null)
            {
                var parentMatrix = pose[bone.Parent.Index];

                ShapeSceneNode.AddLine(vertices, boneMatrix.Translation, parentMatrix.Translation, Color32.White);
            }

            DrawAxes(vertices, boneMatrix, distance, sizeCap);

            foreach (var child in bone.Children)
            {
                DrawSkeletonRecursive(child, vertices, camera, textRenderer);
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
            {
                return;
            }

            if (context.RenderPass != RenderPass.Opaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;

            using var _ = GraphicsContext.RenderState.Scope(depthFunc: RsComparison.Always);

            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            lineBuffer.Draw(Id);
        }
    }
}
