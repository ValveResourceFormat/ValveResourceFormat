using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    public class VisibilitySceneNode : SceneNode
    {
        private readonly record struct ClusterDrawRange(int Start, int Count, ushort ClusterId);

        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int totalVertexCount;
        private readonly ClusterDrawRange[] clusterDrawRanges;

        public VisibilitySceneNode(Scene scene, VoxelVisibility voxelVisibility) : base(scene)
        {
            shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.default");

            var vertices = new List<SimpleVertex>();
            var ranges = new List<ClusterDrawRange>();

            foreach (var (clusterId, children) in voxelVisibility.BuildClusterChildBounds())
            {
                var start = vertices.Count;
                var color = GetClusterColor(clusterId);

                foreach (var (min, max) in children)
                {
                    ShapeSceneNode.AddBox(vertices, new AABB(min, max), color);
                }

                if (vertices.Count > start)
                {
                    ranges.Add(new(start, vertices.Count - start, clusterId));
                }
            }

            clusterDrawRanges = [.. ranges];
            totalVertexCount = vertices.Count;

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, totalVertexCount * SimpleVertex.SizeInBytes,
                ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.StaticDraw);

#if DEBUG
            var label = nameof(VisibilitySceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, label.Length, label);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, label.Length, label);
#endif

            LocalBoundingBox = new AABB(voxelVisibility.MinBounds, voxelVisibility.MaxBounds);
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (totalVertexCount == 0 || context.RenderPass is not RenderPass.Translucent and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.BindVertexArray(vaoHandle);

            if (Scene.CurrentFramePvs == null)
            {
                GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, 0, totalVertexCount, 1, Id);
            }
            else
            {
                foreach (var range in clusterDrawRanges)
                {
                    if (range.ClusterId < (uint)(Scene.CurrentFramePvs.Length * 8) && (Scene.CurrentFramePvs[range.ClusterId >> 3] & (1 << (range.ClusterId & 7))) != 0)
                    {
                        GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, range.Start, range.Count, 1, Id);
                    }
                }
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
        }

        private static Color32 GetClusterColor(ushort clusterId)
        {
            var h = MurmurHash2.Hash(clusterId, 0x3501A674);

            var r = (byte)(((h & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);
            var g = (byte)((((h >> 10) & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);
            var b = (byte)((((h >> 20) & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);

            return new Color32(r, g, b, 255);
        }
    }
}
