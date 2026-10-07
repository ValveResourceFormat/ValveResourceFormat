using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Thins the instances of every <see cref="SceneClutter"/> in a scene on the GPU. Each frame one dispatch tests
    /// every instance against the camera, writes the transforms of the ones to draw into the start of their node's
    /// run in the scene transform buffer, and counts them into the node's indirect draw commands.
    /// </summary>
    internal sealed class ClutterCuller
    {
        /// <summary>Static data of one instance, matching <c>ClutterInstance_t</c> in <c>cull_clutter.comp</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct InstanceGpu
        {
            public OpenTK.Mathematics.Matrix3x4 Transform;
            public Vector3 Center;
            public float Radius;
            public uint IndexInTile;
            public uint TileInstanceCount;
            public uint Batch;
            public uint Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BatchGpu
        {
            public uint TransformBase;
            public uint FirstCommand;
            public uint CommandCount;
            public float EndSize;
            public float NegativeHalfEndCullSize;
            public float BeginSize;
            public uint Padding0;
            public uint Padding1;
        }

        private readonly Shader Shader;
        private readonly StorageBuffer Instances;
        private readonly StorageBuffer Batches;
        private readonly DrawElementsIndirectCommand[] CommandTemplate;
        private readonly int InstanceCount;
        private StorageBuffer? Commands;

        public ClutterCuller(RendererContext rendererContext, IReadOnlyList<SceneClutter> nodes, ReadOnlySpan<InstanceDataStandard> instanceData)
        {
            var instances = new List<InstanceGpu>();
            var batches = new BatchGpu[nodes.Count];
            var commands = new List<DrawElementsIndirectCommand>();

            for (var batch = 0; batch < nodes.Count; batch++)
            {
                var node = nodes[batch];
                var drawCalls = node.RenderMesh.DrawCallsOpaque;

                var endSize = Math.Clamp(node.EndCullSize, 0f, 1f);

                node.FirstCommand = commands.Count;

                batches[batch] = new BatchGpu
                {
                    TransformBase = instanceData[(int)node.Id].TransformIndex,
                    FirstCommand = (uint)commands.Count,
                    CommandCount = (uint)drawCalls.Count,
                    EndSize = endSize,
                    NegativeHalfEndCullSize = -0.5f * node.EndCullSize,
                    BeginSize = Math.Clamp(node.BeginCullSize, endSize, 1f),
                };

                foreach (var drawCall in drawCalls)
                {
                    commands.Add(new DrawElementsIndirectCommand
                    {
                        Count = (uint)drawCall.IndexCount,
                        FirstIndex = (uint)(drawCall.StartIndex / drawCall.IndexSizeInBytes),
                        BaseVertex = drawCall.BaseVertex,
                        BaseInstance = node.Id,
                    });
                }

                foreach (var instance in node.CullInstances)
                {
                    instances.Add(instance with { Batch = (uint)batch });
                }
            }

            Shader = rendererContext.ShaderLoader.LoadShader("cull_clutter");
            InstanceCount = instances.Count;
            CommandTemplate = [.. commands];

            Instances = new StorageBuffer(ReservedBufferSlots.BufferSlot6, "ClutterInstances");
            Instances.Create(instances, BufferUsage.Static);

            Batches = new StorageBuffer(ReservedBufferSlots.BufferSlot7, "ClutterBatches");
            Batches.Create<BatchGpu>(batches, BufferUsage.Static);
        }

        /// <summary>Picks the instances to draw as seen from the camera. Every view of the frame, shadows included, draws this result.</summary>
        public void Cull(Camera camera, StorageBuffer transformBuffer)
        {
            Commands = Scene.Upload<DrawElementsIndirectCommand>(Commands, CommandTemplate, ReservedBufferSlots.BufferSlot8, "ClutterDrawCommands");

            Shader.Use();
            Shader.SetUniform("g_vCameraPosition", camera.Location);
            Shader.SetUniform("g_flProjectionScale", camera.ProjectionMatrix.M11);
            Shader.SetUniform("g_nInstanceCount", (uint)InstanceCount);

            transformBuffer.BindBufferBase();
            Instances.BindBufferBase();
            Batches.BindBufferBase();
            Commands.BindBufferBase();

            GL.DispatchCompute(MathUtils.DivideRoundUp(InstanceCount, 64), 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        /// <summary>Draws one draw call of a clutter node with the instance count the last <see cref="Cull"/> wrote.</summary>
        public void Draw(SceneClutter node, DrawCall drawCall)
        {
            if (Commands is null)
            {
                return;
            }

            var command = node.FirstCommand + node.RenderMesh.DrawCallsOpaque.IndexOf(drawCall);

            GL.GetInteger(GetPName.DrawIndirectBufferBinding, out var previousBuffer);
            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, Commands.Handle);
            GL.DrawElementsIndirect(drawCall.PrimitiveType, drawCall.IndexType, command * Unsafe.SizeOf<DrawElementsIndirectCommand>());
            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, previousBuffer);
        }

        public void Delete()
        {
            Instances.Delete();
            Batches.Delete();
            Commands?.Delete();
        }
    }
}
