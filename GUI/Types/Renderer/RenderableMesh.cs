using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class RenderableMesh
    {
        public AABB BoundingBox { get; }
        public Vector4 Tint { get; set; } = Vector4.One;

        private readonly VrfGuiContext guiContext;
        public List<DrawCall> DrawCallsOpaque { get; } = new List<DrawCall>();
        public List<DrawCall> DrawCallsBlended { get; } = new List<DrawCall>();
        public int? AnimationTexture { get; private set; }
        public int AnimationTextureSize { get; private set; }

        public float Time { get; private set; }

        private Mesh mesh;
        private List<DrawCall> DrawCallsAll = new List<DrawCall>();

        public RenderableMesh(Mesh mesh, VrfGuiContext guiContext, Dictionary<string, string> skinMaterials = null)
        {
            this.guiContext = guiContext;
            this.mesh = mesh;
            BoundingBox = new AABB(mesh.MinBounds, mesh.MaxBounds);

            SetupDrawCalls(mesh, skinMaterials);
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => DrawCallsAll
                .SelectMany(drawCall => drawCall.Shader.RenderModes)
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            var drawCalls = DrawCallsAll;

            foreach (var call in drawCalls)
            {
                // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                var parameters = call.Shader.Parameters
                    .Where(kvp => !kvp.Key.StartsWith("renderMode"))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (renderMode != null && call.Shader.RenderModes.Contains(renderMode))
                {
                    parameters.Add($"renderMode_{renderMode}", true);
                }

                call.Shader = guiContext.ShaderLoader.LoadShader(call.Shader.Name, parameters);
                call.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                    mesh.VBIB,
                    call.Shader,
                    call.VertexBuffer.Id,
                    call.IndexBuffer.Id);
            }
        }

        public void SetAnimationTexture(int? texture, int animationTextureSize)
        {
            AnimationTexture = texture;
            AnimationTextureSize = animationTextureSize;
        }

        public void SetSkin(Dictionary<string, string> skinMaterials)
        {
            var data = mesh.GetData();
            var sceneObjects = data.GetArray("m_sceneObjects");

            int i = 0;
            foreach (var sceneObject in sceneObjects)
            {
                var objectDrawCalls = sceneObject.GetArray("m_drawCalls");

                foreach (var objectDrawCall in objectDrawCalls)
                {
                    var materialName = objectDrawCall.GetProperty<string>("m_material") ?? objectDrawCall.GetProperty<string>("m_pMaterial");

                    if (skinMaterials != null && skinMaterials.ContainsKey(materialName))
                    {
                        materialName = skinMaterials[materialName];
                    }

                    var material = guiContext.MaterialLoader.GetMaterial(materialName);
                    var isOverlay = material.Material.IntParams.ContainsKey("F_OVERLAY");

                    // Ignore overlays for now
                    if (isOverlay)
                    {
                        continue;
                    }

                    var shaderArguments = new Dictionary<string, bool>();

                    if (Mesh.IsCompressedNormalTangent(objectDrawCall))
                    {
                        shaderArguments.Add("fulltangent", false);
                    }

                    SetupDrawCallMaterial(DrawCallsAll[i++], shaderArguments, material);
                }
            }
        }

        public void Update(float timeStep)
        {
            Time += timeStep;
        }

        private void SetupDrawCalls(Mesh mesh, Dictionary<string, string> skinMaterials)
        {
            var vbib = mesh.VBIB;
            var data = mesh.GetData();
            var gpuMeshBuffers = guiContext.MeshBufferCache.GetVertexIndexBuffers(vbib);

            //Prepare drawcalls
            var sceneObjects = data.GetArray("m_sceneObjects");

            foreach (var sceneObject in sceneObjects)
            {
                var objectDrawCalls = sceneObject.GetArray("m_drawCalls");

                foreach (var objectDrawCall in objectDrawCalls)
                {
                    var materialName = objectDrawCall.GetProperty<string>("m_material") ?? objectDrawCall.GetProperty<string>("m_pMaterial");

                    if (skinMaterials != null && skinMaterials.ContainsKey(materialName))
                    {
                        materialName = skinMaterials[materialName];
                    }

                    var material = guiContext.MaterialLoader.GetMaterial(materialName);
                    var isOverlay = material.Material.IntParams.ContainsKey("F_OVERLAY");

                    // Ignore overlays for now
                    if (isOverlay)
                    {
                        continue;
                    }

                    var shaderArguments = new Dictionary<string, bool>();

                    if (Mesh.IsCompressedNormalTangent(objectDrawCall))
                    {
                        shaderArguments.Add("fulltangent", false);
                    }

                    // TODO: Don't pass around so much shit
                    var drawCall = CreateDrawCall(objectDrawCall, vbib, shaderArguments, material);

                    DrawCallsAll.Add(drawCall);
                    if (drawCall.Material.IsBlended)
                    {
                        DrawCallsBlended.Add(drawCall);
                    }
                    else
                    {
                        DrawCallsOpaque.Add(drawCall);
                    }
                }
            }

            //drawCalls = drawCalls.OrderBy(x => x.Material.Parameters.Name).ToList();
        }

        private DrawCall CreateDrawCall(IKeyValueCollection objectDrawCall, VBIB vbib, IDictionary<string, bool> shaderArguments, RenderMaterial material)
        {
            var drawCall = new DrawCall();
 
            string primitiveType = objectDrawCall.GetProperty<object>("m_nPrimitiveType") switch
            {
                string primitiveTypeString => primitiveTypeString,
                byte primitiveTypeByte =>
                (primitiveTypeByte == 5) ? "RENDER_PRIM_TRIANGLES" : ("UNKNOWN_" + primitiveTypeByte),
                _ => throw new NotImplementedException("Unknown PrimitiveType in drawCall!")
            };

            switch (primitiveType)
            {
                case "RENDER_PRIM_TRIANGLES":
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                    break;
                default:
                    throw new NotImplementedException("Unknown PrimitiveType in drawCall! (" + primitiveType + ")");
            }

            SetupDrawCallMaterial(drawCall, shaderArguments, material);

            var indexBufferObject = objectDrawCall.GetSubCollection("m_indexBuffer");

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = Convert.ToUInt32(indexBufferObject.GetProperty<object>("m_hBuffer"));
            indexBuffer.Offset = Convert.ToUInt32(indexBufferObject.GetProperty<object>("m_nBindOffsetBytes"));
            drawCall.IndexBuffer = indexBuffer;

            var indexElementSize = vbib.IndexBuffers[(int)drawCall.IndexBuffer.Id].ElementSizeInBytes;
            //drawCall.BaseVertex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nBaseVertex"));
            //drawCall.VertexCount = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nVertexCount"));
            drawCall.StartIndex = Convert.ToUInt32(objectDrawCall.GetProperty<object>("m_nStartIndex")) * indexElementSize;
            drawCall.IndexCount = Convert.ToInt32(objectDrawCall.GetProperty<object>("m_nIndexCount"));

            if (objectDrawCall.ContainsKey("m_vTintColor"))
            {
                var tintColor = objectDrawCall.GetSubCollection("m_vTintColor").ToVector3();
                drawCall.TintColor = new OpenTK.Vector3(tintColor.X, tintColor.Y, tintColor.Z);
            }

            if (indexElementSize == 2)
            {
                //shopkeeper_vr
                drawCall.IndexType = DrawElementsType.UnsignedShort;
            }
            else if (indexElementSize == 4)
            {
                //glados
                drawCall.IndexType = DrawElementsType.UnsignedInt;
            }
            else
            {
                throw new Exception("Unsupported index type");
            }

            var m_vertexBuffer = objectDrawCall.GetArray("m_vertexBuffers")[0]; // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = Convert.ToUInt32(m_vertexBuffer.GetProperty<object>("m_hBuffer"));
            vertexBuffer.Offset = Convert.ToUInt32(m_vertexBuffer.GetProperty<object>("m_nBindOffsetBytes"));
            drawCall.VertexBuffer = vertexBuffer;

            drawCall.VertexArrayObject = guiContext.MeshBufferCache.GetVertexArrayObject(
                vbib,
                drawCall.Shader,
                drawCall.VertexBuffer.Id,
                drawCall.IndexBuffer.Id);

            return drawCall;
        }

        private void SetupDrawCallMaterial(DrawCall drawCall, IDictionary<string, bool> shaderArguments, RenderMaterial material)
        {
            drawCall.Material = material;
            // Add shader parameters from material to the shader parameters from the draw call
            var combinedShaderParameters = shaderArguments
                .Concat(material.Material.GetShaderArguments())
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Load shader
            drawCall.Shader = guiContext.ShaderLoader.LoadShader(drawCall.Material.Material.ShaderName, combinedShaderParameters);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader.Program);

            if (!drawCall.Material.Textures.ContainsKey("g_tTintMask"))
            {
                drawCall.Material.Textures.Add("g_tTintMask", MaterialLoader.CreateSolidTexture(1f, 1f, 1f));
            }

            if (!drawCall.Material.Textures.ContainsKey("g_tNormal"))
            {
                drawCall.Material.Textures.Add("g_tNormal", MaterialLoader.CreateSolidTexture(0.5f, 1f, 0.5f));
            }
        }
    }

    internal interface IRenderableMeshCollection
    {
        IEnumerable<RenderableMesh> RenderableMeshes { get; }
    }
}
