using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes selected model attachments and their bone influences.
    /// </summary>
    public class AttachmentSceneNode : SceneNode
    {
        private bool enabled;
        private readonly ModelSceneNode modelNode;
        private readonly string attachmentName;
        private readonly Attachment attachment;
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

        /// <summary>Gets the attachment name shown in the viewer.</summary>
        public string AttachmentName => attachmentName;

        /// <summary>
        /// Initializes a new instance of the <see cref="AttachmentSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="modelNode">The model that owns the attachment.</param>
        /// <param name="attachmentName">The attachment name.</param>
        /// <param name="attachment">The attachment definition to visualize.</param>
        public AttachmentSceneNode(Scene scene, ModelSceneNode modelNode, string attachmentName, Attachment attachment)
            : base(scene)
        {
            this.modelNode = modelNode;
            this.attachmentName = attachmentName;
            this.attachment = attachment;
            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(SkeletonSceneNode));
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var attachmentTransform = modelNode.GetAttachmentTransform(attachmentName);
            var attachmentPosition = attachmentTransform.Translation;
            var vertices = new List<SimpleVertex>();

            var sizeCap = modelNode.BoundingBox.Size.Length();
            if (sizeCap < 1f)
            {
                sizeCap = 100f;
            }

            var distance = Vector3.Distance(context.Camera.Location, attachmentPosition);
            var distanceFade = distance > sizeCap ? sizeCap / distance : 1f;

            var label = string.IsNullOrEmpty(attachment.Name) ? attachmentName : attachment.Name;
            context.TextRenderer.AddTextBillboard(attachmentPosition, new TextRenderer.TextRenderRequest
            {
                Scale = 10f * distanceFade,
                Text = label,
                Color = new Color32(1.0f, 0.9f, 0.7f, 1.0f),
            }, context.Camera);

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
    }
}
