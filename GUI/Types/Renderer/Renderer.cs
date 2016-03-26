using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;
using Timer = System.Timers.Timer;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace GUI.Types.Renderer
{
    internal class Renderer
    {
        private bool Loaded;

        private readonly Package CurrentPackage;
        private readonly string CurrentFileName;
        private readonly BinaryKV3 data;
        private readonly ArgumentDependencies modelArguments;
        private readonly VBIB block;

        private GLControl meshControl;

        private Camera ActiveCamera;
        private readonly TabControl tabs;

        private readonly List<DrawCall> drawCalls = new List<DrawCall>();

        private Vector3 MinBounds;
        private Vector3 MaxBounds;

        private int MaxTextureMaxAnisotropy;

        private MaterialLoader MaterialLoader;

        public Renderer(Resource resource, TabControl mainTabs, string fileName, Package currentPackage)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = fileName;
            block = resource.VBIB;
            data = (BinaryKV3)resource.Blocks[BlockType.DATA];
            modelArguments = (ArgumentDependencies)((ResourceEditInfo)resource.Blocks[BlockType.REDI]).Structs[ResourceEditInfo.REDIStruct.ArgumentDependencies];
            tabs = mainTabs;

            MaterialLoader = new MaterialLoader(CurrentFileName, CurrentPackage);
        }

        public Control CreateGL()
        {
#if DEBUG
            meshControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Debug);
#else
            meshControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Default);
#endif
            meshControl.Dock = DockStyle.Fill;
            meshControl.AutoSize = true;
            meshControl.Load += MeshControl_Load;
            meshControl.Paint += MeshControl_Paint;
            meshControl.Resize += MeshControl_Resize;
            meshControl.MouseEnter += MeshControl_MouseEnter;
            meshControl.MouseLeave += MeshControl_MouseLeave;
            meshControl.GotFocus += MeshControl_GotFocus;
            return meshControl;
        }

        private void MeshControl_GotFocus(object sender, EventArgs e)
        {
            meshControl.MakeCurrent();
            meshControl.SwapBuffers();
            meshControl.VSync = true;
        }

        private void MeshControl_MouseLeave(object sender, EventArgs e)
        {
            ActiveCamera.MouseOverRenderArea = false;
        }

        private void MeshControl_MouseEnter(object sender, EventArgs e)
        {
            ActiveCamera.MouseOverRenderArea = true;
        }

        private void MeshControl_Resize(object sender, EventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            ActiveCamera.SetViewportSize(tabs.Width, tabs.Height);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            meshControl.SwapBuffers();
        }

        private void InitializeInputTick()
        {
            var timer = new Timer();
            timer.Enabled = true;
            timer.Interval = 1000 / 60;
            timer.Elapsed += InputTick;
            timer.Start();
        }

        private void InputTick(object sender, EventArgs e)
        {
            ActiveCamera.HandleInput(Mouse.GetState(), Keyboard.GetState());
        }

        public void CheckOpenGL()
        {
            var extensions = new Dictionary<string, bool>();
            int count = GL.GetInteger(GetPName.NumExtensions);
            for (int i = 0; i < count; i++)
            {
                string extension = GL.GetString(StringNameIndexed.Extensions, i);
                extensions.Add(extension, true);
            }

            if (extensions.ContainsKey("GL_EXT_texture_filter_anisotropic"))
            {
                MaxTextureMaxAnisotropy = GL.GetInteger((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
            }
            else
            {
                Console.Error.WriteLine("GL_EXT_texture_filter_anisotropic is not supported");
            }
        }

        private void MeshControl_Load(object sender, EventArgs e)
        {
            meshControl.MakeCurrent();

            Console.WriteLine("OpenGL version: " + GL.GetString(StringName.Version));
            Console.WriteLine("OpenGL vendor: " + GL.GetString(StringName.Vendor));
            Console.WriteLine("GLSL version: " + GL.GetString(StringName.ShadingLanguageVersion));

            CheckOpenGL();
            LoadBoundingBox();

            GL.Enable(EnableCap.DepthTest);

            GL.ClearColor(Settings.BackgroundColor);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            InitializeInputTick();

            ActiveCamera = new Camera(tabs.Width, tabs.Height, MinBounds, MaxBounds);

            Console.WriteLine("Setting up buffers..");

            var vertexBuffers = new uint[block.VertexBuffers.Count];
            var indexBuffers = new uint[block.IndexBuffers.Count];

            GL.GenBuffers(block.VertexBuffers.Count, vertexBuffers);
            GL.GenBuffers(block.IndexBuffers.Count, indexBuffers);

            Console.WriteLine(block.VertexBuffers.Count + " vertex buffers");
            Console.WriteLine(block.IndexBuffers.Count + " index buffers");

            for (var i = 0; i < block.VertexBuffers.Count; i++)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[i]);
                GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(block.VertexBuffers[i].Count * block.VertexBuffers[i].Size), block.VertexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                var verticeBufferSize = 0;
                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out verticeBufferSize);
            }

            for (var i = 0; i < block.IndexBuffers.Count; i++)
            {
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[i]);
                GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(block.IndexBuffers[i].Count * block.IndexBuffers[i].Size), block.IndexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                var indiceBufferSize = 0;
                GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out indiceBufferSize);
            }

            Console.WriteLine("Pushed buffers");

            //Prepare drawcalls
            var a = (KVObject)data.Data.Properties["m_sceneObjects"].Value;
            var b = (KVObject)a.Properties["0"].Value;
            var c = (KVObject)b.Properties["m_drawCalls"].Value;

            for (var i = 0; i < c.Properties.Count; i++)
            {
                var d = (KVObject)c.Properties[i.ToString()].Value;
                var drawCall = default(DrawCall);

                switch (d.Properties["m_nPrimitiveType"].Value.ToString())
                {
                    case "RENDER_PRIM_TRIANGLES":
                        drawCall.PrimitiveType = PrimitiveType.Triangles;
                        break;
                    default:
                        throw new Exception("Unknown PrimitiveType in drawCall! (" + d.Properties["m_nPrimitiveType"].Value + ")");
                }

                drawCall.BaseVertex = Convert.ToUInt32(d.Properties["m_nBaseVertex"].Value);
                drawCall.VertexCount = Convert.ToUInt32(d.Properties["m_nVertexCount"].Value);
                drawCall.StartIndex = Convert.ToUInt32(d.Properties["m_nStartIndex"].Value);
                drawCall.IndexCount = Convert.ToUInt32(d.Properties["m_nIndexCount"].Value);

                drawCall.Material = MaterialLoader.GetMaterial(d.Properties["m_material"].Value.ToString(), MaxTextureMaxAnisotropy);

                drawCall.MaterialID = drawCall.Material.TextureIDs["g_tColor"];

                // Load shader
                drawCall.Shader = ShaderLoader.LoadShaders(drawCall.Material.ShaderName, modelArguments);

                //Bind and validate shader
                GL.UseProgram(drawCall.Shader);

                var f = (KVObject)d.Properties["m_indexBuffer"].Value;

                var indexBuffer = default(DrawBuffer);
                indexBuffer.Id = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);
                indexBuffer.Offset = Convert.ToUInt32(f.Properties["m_nBindOffsetBytes"].Value);
                drawCall.IndexBuffer = indexBuffer;

                if (block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size == 2)
                {
                    //shopkeeper_vr
                    drawCall.IndiceType = DrawElementsType.UnsignedShort;
                }
                else if (block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size == 4)
                {
                    //glados
                    drawCall.IndiceType = DrawElementsType.UnsignedInt;
                }
                else
                {
                    throw new Exception("Unsupported indice type");
                }

                var g = (KVObject)d.Properties["m_vertexBuffers"].Value;
                var h = (KVObject)g.Properties["0"].Value;

                var vertexBuffer = default(DrawBuffer);
                vertexBuffer.Id = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);
                vertexBuffer.Offset = Convert.ToUInt32(h.Properties["m_nBindOffsetBytes"].Value);
                drawCall.VertexBuffer = vertexBuffer;

                GL.GenVertexArrays(1, out drawCall.VertexArrayObject);

                GL.BindVertexArray(drawCall.VertexArrayObject);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[drawCall.VertexBuffer.Id]);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[drawCall.IndexBuffer.Id]);

                var curVertexBuffer = block.VertexBuffers[(int)drawCall.VertexBuffer.Id];
                var texcoordSet = false;
                foreach (var attribute in curVertexBuffer.Attributes)
                {
                    switch (attribute.Name)
                    {
                        case "POSITION":
                            BindVertexAttrib(attribute, "vPosition", drawCall.Shader, (int)curVertexBuffer.Size);
                            break;

                        case "NORMAL":
                            BindVertexAttrib(attribute, "vNormal", drawCall.Shader, (int)curVertexBuffer.Size);
                            break;

                        case "TEXCOORD":
                            // Ignore second set of texcoords
                            if (texcoordSet)
                            {
                                break;
                            }

                            BindVertexAttrib(attribute, "vTexCoord", drawCall.Shader, (int)curVertexBuffer.Size);

                            texcoordSet = true;
                            break;
                        case "TANGENT":
                            BindVertexAttrib(attribute, "vTangent", drawCall.Shader, (int)curVertexBuffer.Size);
                            break;

                        case "BLENDINDICES":
                            BindVertexAttrib(attribute, "vBlendIndices", drawCall.Shader, (int)curVertexBuffer.Size);
                            break;

                        case "BLENDWEIGHT":
                            BindVertexAttrib(attribute, "vBlendWeight", drawCall.Shader, (int)curVertexBuffer.Size);
                            break;
                    }
                }

                if (drawCall.Material.IntParams.ContainsKey("F_ALPHA_TEST") && drawCall.Material.IntParams["F_ALPHA_TEST"] == 1)
                {
                    GL.Enable(EnableCap.AlphaTest);

                    if (drawCall.Material.FloatParams.ContainsKey("g_flAlphaTestReference"))
                    {
                        var alphaReference = GL.GetUniformLocation(drawCall.Shader, "alphaReference");
                        GL.Uniform1(alphaReference, drawCall.Material.FloatParams["g_flAlphaTestReference"]);
                    }
                }

                if (drawCall.Material.IntParams.ContainsKey("F_TRANSLUCENT") && drawCall.Material.IntParams["F_TRANSLUCENT"] == 1)
                {
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                }

                GL.BindVertexArray(0);
                GL.EnableVertexAttribArray(drawCall.VertexArrayObject);

                drawCalls.Add(drawCall);
            }

            Loaded = true;
        }

        private void MeshControl_Paint(object sender, PaintEventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            ActiveCamera.Tick();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            foreach (var call in drawCalls)
            {
                //Bind shader
                GL.UseProgram(call.Shader);

                //Set shader uniforms
                var transformLoc = GL.GetUniformLocation(call.Shader, "projection");
                GL.UniformMatrix4(transformLoc, false, ref ActiveCamera.ProjectionMatrix);

                var modelviewLoc = GL.GetUniformLocation(call.Shader, "modelview");
                GL.UniformMatrix4(modelviewLoc, false, ref ActiveCamera.CameraViewMatrix);

                var lightPosAttrib = GL.GetUniformLocation(call.Shader, "vLightPosition");
                GL.Uniform3(lightPosAttrib, ActiveCamera.Location);

                //Bind VAO
                GL.BindVertexArray(call.VertexArrayObject);

                //Set shader texture samplers
                //Color texture
                TryToBindTexture(call.Shader, 0, "colorTexture", call.MaterialID);

                if (call.Material.TextureIDs.ContainsKey("g_tNormal"))
                {
                    //Bind normal texture
                    TryToBindTexture(call.Shader, 1, "normalTexture", call.Material.TextureIDs["g_tNormal"]);
                }

                if (call.Material.TextureIDs.ContainsKey("g_tMask1"))
                {
                    //Bind normal texture
                    TryToBindTexture(call.Shader, 2, "mask1Texture", call.Material.TextureIDs["g_tMask1"]);
                }

                if (call.Material.TextureIDs.ContainsKey("g_tMask2"))
                {
                    //Bind normal texture
                    TryToBindTexture(call.Shader, 3, "mask2Texture", call.Material.TextureIDs["g_tMask2"]);
                }

                GL.DrawElements(call.PrimitiveType, (int)call.IndexCount, call.IndiceType, IntPtr.Zero);
            }

            // Only needed when debugging if something doesnt work, causes high CPU
            /*
            var error = GL.GetError();

            if (error != ErrorCode.NoError)
            {
                Console.WriteLine(error);
            }
            */

            meshControl.SwapBuffers();
            meshControl.Invalidate();
        }

        private void TryToBindTexture(int shader, int textureUnit, string uniform, int textureID)
        {
            //Get uniform location from the shader
            var uniformLocation = GL.GetUniformLocation(shader, uniform);

            //Stop if the uniform loction does not exist
            if (uniformLocation == -1)
            {
                return;
            }

            //Bind texture unit and texture
            GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
            GL.BindTexture(TextureTarget.Texture2D, textureID);

            //Set uniform location
            GL.Uniform1(uniformLocation, textureUnit);
        }

        private void LoadBoundingBox()
        {
            var a = (KVObject)data.Data.Properties["m_sceneObjects"].Value;
            var b = (KVObject)a.Properties["0"].Value;
            var minBounds = (KVObject)b.Properties["m_vMinBounds"].Value;
            var maxBounds = (KVObject)b.Properties["m_vMaxBounds"].Value;

            MaxBounds.X = (float)Convert.ToDouble(maxBounds.Properties["0"].Value);
            MinBounds.X = (float)Convert.ToDouble(minBounds.Properties["0"].Value);
            MaxBounds.Y = (float)Convert.ToDouble(maxBounds.Properties["1"].Value);
            MinBounds.Y = (float)Convert.ToDouble(minBounds.Properties["1"].Value);
            MaxBounds.Z = (float)Convert.ToDouble(maxBounds.Properties["2"].Value);
            MinBounds.Z = (float)Convert.ToDouble(minBounds.Properties["2"].Value);
        }

        private void BindVertexAttrib(VBIB.VertexAttribute attribute, string attributeName, int shaderProgram, int stride)
        {
            var attributeLocation = GL.GetAttribLocation(shaderProgram, attributeName);

            //Ignore this attribute if it is not found in the shader
            if (attributeLocation == -1)
            {
                return;
            }

            GL.EnableVertexAttribArray(attributeLocation);

            switch (attribute.Type)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.HalfFloat, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.UnsignedInt, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 2, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.UnsignedShort, true, stride, (IntPtr)attribute.Offset);
                    break;

                default:
                    throw new Exception("Unknown attribute format " + attribute.Type);
            }
        }
    }
}
