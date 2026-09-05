using SkiaSharp;
using Attachment = ValveResourceFormat.ResourceTypes.ModelData.Attachments.Attachment;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes selected model attachments and their bone influences.
    /// </summary>
    public class AttachmentsSceneNode : SceneNode
    {
        private bool enabled;
        private readonly ModelSceneNode modelNode;
        private readonly LineBuffer lineBuffer;

        /// <summary>Gets or sets whether the attachment visualization is drawn.</summary>
        public bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
            }
        }
        /// <summary>Set of visible attachments.</summary>
        public HashSet<string> SelectedAttachments { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AttachmentsSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="modelNode">The model that owns the attachment.</param>
        public AttachmentsSceneNode(Scene scene, ModelSceneNode modelNode)
            : base(scene)
        {
            this.modelNode = modelNode;
            SelectedAttachments = [];
            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(SkeletonSceneNode));
        }

        private void DrawAttachment(string attachmentName, List<SimpleVertex> vertices, Camera camera, TextRenderer textRenderer)
        {
            var attachment = modelNode.Attachments.GetValueOrDefault(attachmentName);
            if (attachment == null)
            {
                return;
            }

            var attachmentTransform = modelNode.GetAttachmentTransform(attachmentName);
            var attachmentPosition = attachmentTransform.Translation;

            var sizeCap = modelNode.BoundingBox.Size.Length();
            if (sizeCap < 1f)
            {
                sizeCap = 100f;
            }

            var distance = Vector3.Distance(camera.Location, attachmentPosition);
            var distanceFade = distance > sizeCap ? sizeCap / distance : 1f;

            var label = string.IsNullOrEmpty(attachment.Name) ? attachmentName : attachment.Name;
            textRenderer.AddTextBillboard(attachmentPosition, new TextRenderer.TextRenderRequest
            {
                Scale = 10f * distanceFade,
                Text = label,
                Color = new Color32(1.0f, 0.9f, 0.7f, 1.0f),
            }, camera);

            if (attachment.Length > 0)
            {
                foreach (var influence in attachment)
                {
                    var boneIndex = modelNode.AnimationController.FrameCache.Skeleton.GetBoneIndex(influence.Name);
                    if (boneIndex == -1)
                    {
                        continue;
                    }

                    var bonePosition = modelNode.AnimationController.Pose[boneIndex].Translation;
                    var alpha = Math.Clamp(influence.Weight, 0.15f, 1.0f);
                    ShapeSceneNode.AddLine(vertices, attachmentPosition, bonePosition, new Color32(1.0f, 1.0f, 1.0f, alpha));
                }
            }

            // Attachment-local axes, normalized to strip any scale from the pose.
            // Sized proportionally to camera distance so they stay constant on screen.
            var axisLength = 0.04f * MathF.Min(distance, sizeCap);
            ShapeSceneNode.AddLine(vertices, attachmentPosition, attachmentPosition + Vector3.Normalize(new Vector3(attachmentTransform.M11, attachmentTransform.M12, attachmentTransform.M13)) * axisLength, new(1.0f, 0.2f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, attachmentPosition, attachmentPosition + Vector3.Normalize(new Vector3(attachmentTransform.M21, attachmentTransform.M22, attachmentTransform.M23)) * axisLength, new(0.2f, 0.8f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, attachmentPosition, attachmentPosition + Vector3.Normalize(new Vector3(attachmentTransform.M31, attachmentTransform.M32, attachmentTransform.M33)) * axisLength, new(0.2f, 0.2f, 1.0f, 1.0f));
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var vertices = new List<SimpleVertex>();

            foreach (var attachmentName in SelectedAttachments)
            {
                DrawAttachment(attachmentName, vertices, context.Camera, context.TextRenderer);
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

        /// <summary>
        /// Enable rendering of attachment in the viewport by name.
        /// </summary>
        /// <param name="attachments"></param>
        public void SetAttachmentVisibility(HashSet<string> attachments)
        {
            SelectedAttachments = attachments;
        }
    }
}
