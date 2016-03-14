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

namespace GUI.Types.Renderer
{
    internal class Renderer
    {
        private bool Loaded;

        private uint[] vertexBuffers;
        private uint[] indexBuffers;

        private int shaderProgram;

        private readonly Package CurrentPackage;
        private readonly string CurrentFileName;
        private readonly BinaryKV3 data;
        private readonly VBIB block;

        private GLControl meshControl;

        private Camera ActiveCamera;
        private readonly TabControl tabs;

        private readonly List<DrawCall> drawCalls = new List<DrawCall>();

        private Vector3 MinBounds;
        private Vector3 MaxBounds;

        private int MaxTextureMaxAnisotropy;

        private readonly Vector3 LightPosition = new Vector3(0.0f, 0.0f, 0.0f);

        private struct DrawCall
        {
            public PrimitiveType PrimitiveType;
            public uint BaseVertex;
            public uint VertexCount;
            public uint StartIndex;
            public uint IndexCount;
            public uint InstanceIndex;   //TODO
            public uint InstanceCount;   //TODO
            public float UvDensity;     //TODO
            public string Flags;        //TODO
            public Vector3 TintColor;   //TODO
            public string Material;
            public int MaterialID;
            public uint VertexArrayObject;
            public DrawBuffer VertexBuffer;
            public DrawElementsType IndiceType;
            public DrawBuffer IndexBuffer;
        }

        private struct DrawBuffer
        {
            public uint Id;
            public uint Offset;
        }

        public Renderer(Resource resource, TabControl mainTabs, string fileName, Package currentPackage)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = fileName;
            block = resource.VBIB;
            data = (BinaryKV3)resource.Blocks[BlockType.DATA];
            tabs = mainTabs;
        }

        public Control CreateGL()
        {
#if DEBUG
            meshControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 0, GraphicsContextFlags.Debug);
#else
            meshControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 0, GraphicsContextFlags.Default);
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
            var transformLoc = GL.GetUniformLocation(shaderProgram, "projection");
            GL.UniformMatrix4(transformLoc, false, ref ActiveCamera.ProjectionMatrix);
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
            var x = new Version(GL.GetString(StringName.Version).Split(' ')[0]);
            var y = new Version(3, 3, 0, 0);

            if (x < y)
            {
                Console.WriteLine("OpenGL {0} or newer required.", y);
            }

            var extensions = GL.GetString(StringName.Extensions).Split(' ');
            if (extensions.Contains("GL_EXT_texture_filter_anisotropic"))
            {
                MaxTextureMaxAnisotropy = GL.GetInteger((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);

                Console.WriteLine("MaxTextureMaxAnisotropy: " + MaxTextureMaxAnisotropy);
            }
        }

        private void MeshControl_Load(object sender, EventArgs e)
        {
            CheckOpenGL();
            LoadBoundingBox();

            meshControl.MakeCurrent();

            Console.WriteLine("OpenGL version: " + GL.GetString(StringName.Version));
            Console.WriteLine("OpenGL vendor: " + GL.GetString(StringName.Vendor));
            Console.WriteLine("GLSL version: " + GL.GetString(StringName.ShadingLanguageVersion));

            GL.Enable(EnableCap.DepthTest);

            GL.ClearColor(Settings.BackgroundColor);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            InitializeInputTick();

            ActiveCamera = new Camera(tabs.Width, tabs.Height, MinBounds, MaxBounds);

            Console.WriteLine("Setting up shaders..");

            ShaderLoader.LoadShaders();
            shaderProgram = ShaderLoader.ShaderProgram;

            GL.UseProgram(shaderProgram);

            GL.ValidateProgram(shaderProgram);

            var transformLoc = GL.GetUniformLocation(shaderProgram, "projection");
            GL.UniformMatrix4(transformLoc, false, ref ActiveCamera.ProjectionMatrix);

            Console.WriteLine("Setting up buffers..");

            vertexBuffers = new uint[block.VertexBuffers.Count];
            indexBuffers = new uint[block.IndexBuffers.Count];

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

                drawCall.Material = d.Properties["m_material"].Value.ToString();

                if (!MaterialLoader.Materials.ContainsKey(drawCall.Material))
                {
                    drawCall.MaterialID = MaterialLoader.LoadMaterial(drawCall.Material, CurrentFileName, CurrentPackage, MaxTextureMaxAnisotropy);
                }
                else
                {
                    drawCall.MaterialID = MaterialLoader.Materials[drawCall.Material].ColorTextureID;
                }

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
                            var posAttrib = GL.GetAttribLocation(shaderProgram, "vPosition");
                            //Ignore this attribute if it is not found in the shader
                            if (posAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(posAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R32G32B32_FLOAT:
                                    GL.VertexAttribPointer(posAttrib, 3, VertexAttribPointerType.Float, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unknown position format " + attribute.Type);
                            }

                            break;
                        case "NORMAL":
                            var normalAttrib = GL.GetAttribLocation(shaderProgram, "vNormal");
                            //Ignore this attribute if it is not found in the shader
                            if (normalAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(normalAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R32G32B32_FLOAT:
                                    GL.VertexAttribPointer(normalAttrib, 3, VertexAttribPointerType.Float, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R8G8B8A8_UNORM:
                                    GL.VertexAttribPointer(normalAttrib, 4, VertexAttribPointerType.UnsignedByte, true, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unsupported normal format " + attribute.Type);
                            }

                            break;
                        case "TEXCOORD":
                            // Ignore second set of texcoords
                            if (texcoordSet)
                            {
                                break;
                            }

                            var texCoordAttrib = GL.GetAttribLocation(shaderProgram, "vTexCoord");
                            //Ignore this attribute if it is not found in the shader
                            if (texCoordAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(texCoordAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R32G32_FLOAT:
                                    GL.VertexAttribPointer(texCoordAttrib, 2, VertexAttribPointerType.Float, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R16G16_FLOAT:
                                    GL.VertexAttribPointer(texCoordAttrib, 2, VertexAttribPointerType.HalfFloat, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unsupported texcoord format " + attribute.Type);
                            }

                            texcoordSet = true;
                            break;
                        case "TANGENT":
                            var tangentAttrib = GL.GetAttribLocation(shaderProgram, "vTangent");
                            //Ignore this attribute if it is not found in the shader
                            if (tangentAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(tangentAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                                    GL.VertexAttribPointer(tangentAttrib, 4, VertexAttribPointerType.Float, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unsupported tangent format " + attribute.Type);
                            }

                            break;
                        case "BLENDINDICES":
                            var blendIndicesAttrib = GL.GetAttribLocation(shaderProgram, "vBlendIndices");
                            //Ignore this attribute if it is not found in the shader
                            if (blendIndicesAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(blendIndicesAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R8G8B8A8_UINT:
                                    GL.VertexAttribIPointer(blendIndicesAttrib, 4, VertexAttribIntegerType.UnsignedInt, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R16G16_SINT:
                                    GL.VertexAttribIPointer(blendIndicesAttrib, 2, VertexAttribIntegerType.Short, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R16G16B16A16_SINT:
                                    GL.VertexAttribIPointer(blendIndicesAttrib, 4, VertexAttribIntegerType.Short, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unsupported blend indices format " + attribute.Type);
                            }

                            break;
                        case "BLENDWEIGHT":
                            var blendWeightAttrib = GL.GetAttribLocation(shaderProgram, "vBlendWeight");
                            //Ignore this attribute if it is not found in the shader
                            if (blendWeightAttrib == -1)
                            {
                                break;
                            }

                            GL.EnableVertexAttribArray(blendWeightAttrib);
                            switch (attribute.Type)
                            {
                                case DXGI_FORMAT.R16G16_UNORM:
                                    GL.VertexAttribPointer(blendWeightAttrib, 2, VertexAttribPointerType.UnsignedShort, true, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R8G8B8A8_UINT:
                                    GL.VertexAttribPointer(blendWeightAttrib, 4, VertexAttribPointerType.UnsignedByte, false, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                case DXGI_FORMAT.R8G8B8A8_UNORM:
                                    GL.VertexAttribPointer(blendWeightAttrib, 4, VertexAttribPointerType.UnsignedByte, true, (int)curVertexBuffer.Size, (IntPtr)attribute.Offset);
                                    break;
                                default:
                                    throw new Exception("Unsupported blend weight format " + attribute.Type);
                            }

                            break;
                    }
                }

                // Don't do material lookups on error texture
                if (drawCall.MaterialID != 1)
                {
                    if (MaterialLoader.Materials[drawCall.Material].IntParams.ContainsKey("F_ALPHA_TEST") && MaterialLoader.Materials[drawCall.Material].IntParams["F_ALPHA_TEST"] == 1)
                    {
                        GL.Enable(EnableCap.AlphaTest);

                        if (MaterialLoader.Materials[drawCall.Material].FloatParams.ContainsKey("g_flAlphaTestReference"))
                        {
                            var alphaReference = GL.GetUniformLocation(shaderProgram, "alphaReference");
                            GL.Uniform1(alphaReference, MaterialLoader.Materials[drawCall.Material].FloatParams["g_flAlphaTestReference"]);
                        }
                    }

                    if (MaterialLoader.Materials[drawCall.Material].IntParams.ContainsKey("F_TRANSLUCENT") && MaterialLoader.Materials[drawCall.Material].IntParams["F_TRANSLUCENT"] == 1)
                    {
                        GL.Enable(EnableCap.Blend);
                        GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                    }

                    var colorTextureAttrib = GL.GetUniformLocation(shaderProgram, "colorTexture");
                    GL.Uniform1(colorTextureAttrib, 0);

                    if (MaterialLoader.Materials[drawCall.Material].OtherTextureIDs.ContainsKey("g_tNormal"))
                    {
                        var normalTextureAttrib = GL.GetUniformLocation(shaderProgram, "normalTexture");
                        GL.Uniform1(normalTextureAttrib, 1);
                    }
                }

                GL.BindVertexArray(0);
                GL.EnableVertexAttribArray(drawCall.VertexArrayObject);

                drawCalls.Add(drawCall);
            }

            var projectionLoc = GL.GetUniformLocation(shaderProgram, "projection");
            GL.UniformMatrix4(projectionLoc, false, ref ActiveCamera.ProjectionMatrix);

            Loaded = true;
        }

        private void MeshControl_Paint(object sender, PaintEventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            ActiveCamera.Tick();

            var modelviewLoc = GL.GetUniformLocation(shaderProgram, "modelview");
            GL.UniformMatrix4(modelviewLoc, false, ref ActiveCamera.CameraViewMatrix);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            foreach (var call in drawCalls)
            {
                GL.BindVertexArray(call.VertexArrayObject);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, call.MaterialID);

                // Don't do material lookups on error texture
                if (call.MaterialID != 1)
                {
                    if (MaterialLoader.Materials[call.Material].OtherTextureIDs.ContainsKey("g_tNormal"))
                    {
                        GL.ActiveTexture(TextureUnit.Texture1);
                        GL.BindTexture(TextureTarget.Texture2D, MaterialLoader.Materials[call.Material].OtherTextureIDs["g_tNormal"]);
                    }
                }

                GL.DrawElements(call.PrimitiveType, (int)call.IndexCount, call.IndiceType, IntPtr.Zero);
            }

            var lightPosAttrib = GL.GetUniformLocation(shaderProgram, "vLightPosition");
            GL.Uniform3(lightPosAttrib, LightPosition);

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
    }
}
