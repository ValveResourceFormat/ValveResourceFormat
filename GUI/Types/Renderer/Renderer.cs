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
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;
using Timer = System.Timers.Timer;

namespace GUI.Types.Renderer
{
    internal class Renderer
    {
        private readonly MaterialLoader MaterialLoader;
        private readonly TabControl tabs;

        private readonly Package CurrentPackage;
        private readonly string CurrentFileName;

        private readonly List<MeshObject> MeshesToRender;

        private bool Loaded;

        private GLControl meshControl;

        private Camera ActiveCamera;

        private Vector3 MinBounds;
        private Vector3 MaxBounds;

        private int MaxTextureMaxAnisotropy;

        public Renderer(TabControl mainTabs, string fileName, Package currentPackage)
        {
            MeshesToRender = new List<MeshObject>();

            CurrentPackage = currentPackage;
            CurrentFileName = fileName;
            tabs = mainTabs;

            MaterialLoader = new MaterialLoader(CurrentFileName, CurrentPackage);
        }

        public void AddMeshObject(MeshObject obj)
        {
            MeshesToRender.Add(obj);
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
            var count = GL.GetInteger(GetPName.NumExtensions);
            for (var i = 0; i < count; i++)
            {
                var extension = GL.GetString(StringNameIndexed.Extensions, i);
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

            foreach (var obj in MeshesToRender)
            {
                var resource = obj.Resource;
                var block = resource.VBIB;
                var data = (BinaryKV3)resource.Blocks[BlockType.DATA];
                var modelArguments = (ArgumentDependencies)((ResourceEditInfo)resource.Blocks[BlockType.REDI]).Structs[ResourceEditInfo.REDIStruct.ArgumentDependencies];

                var vertexBuffers = new uint[block.VertexBuffers.Count];
                var indexBuffers = new uint[block.IndexBuffers.Count];

                GL.GenBuffers(block.VertexBuffers.Count, vertexBuffers);
                GL.GenBuffers(block.IndexBuffers.Count, indexBuffers);

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

                //Prepare drawcalls
                var a = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

                for (var b = 0; b < a.Properties.Count; b++)
                {
                    var c = (KVObject)((KVObject)a.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                    for (var i = 0; i < c.Properties.Count; i++)
                    {
                        var d = (KVObject) c.Properties[i.ToString()].Value;

                        // TODO: Don't pass around so much shit
                        var drawCall = CreateDrawCall(d.Properties, vertexBuffers, indexBuffers, modelArguments, resource.VBIB);
                        obj.DrawCalls.Add(drawCall);
                    }
                }

                obj.Resource = null;
            }

            // TODO: poor hack
            FileExtensions.ClearCache();

            Loaded = true;

            Console.WriteLine("{0} draw calls total", MeshesToRender.Sum(x => x.DrawCalls.Count));
        }

        private DrawCall CreateDrawCall(Dictionary<string, KVValue> drawProperties, uint[] vertexBuffers, uint[] indexBuffers, ArgumentDependencies modelArguments, VBIB block)
        {
            var drawCall = new DrawCall();

            switch (drawProperties["m_nPrimitiveType"].Value.ToString())
            {
                case "RENDER_PRIM_TRIANGLES":
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                    break;
                default:
                    throw new Exception("Unknown PrimitiveType in drawCall! (" + drawProperties["m_nPrimitiveType"].Value + ")");
            }

            drawCall.Material = MaterialLoader.GetMaterial(drawProperties["m_material"].Value.ToString(), MaxTextureMaxAnisotropy);

            // Load shader
            drawCall.Shader = ShaderLoader.LoadShaders(drawCall.Material.ShaderName, modelArguments);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader);

            var f = (KVObject)drawProperties["m_indexBuffer"].Value;

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);
            indexBuffer.Offset = Convert.ToUInt32(f.Properties["m_nBindOffsetBytes"].Value);
            drawCall.IndexBuffer = indexBuffer;

            var bufferSize = block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size;
            drawCall.BaseVertex = Convert.ToUInt32(drawProperties["m_nBaseVertex"].Value);
            drawCall.VertexCount = Convert.ToUInt32(drawProperties["m_nVertexCount"].Value);
            drawCall.StartIndex = Convert.ToUInt32(drawProperties["m_nStartIndex"].Value) * bufferSize;
            drawCall.IndexCount = Convert.ToInt32(drawProperties["m_nIndexCount"].Value);

            if (drawProperties.ContainsKey("m_vTintColor"))
            {
                var tint = (KVObject) drawProperties["m_vTintColor"].Value;
                drawCall.TintColor = new Vector3(
                    Convert.ToSingle(tint.Properties["0"].Value),
                    Convert.ToSingle(tint.Properties["1"].Value),
                    Convert.ToSingle(tint.Properties["2"].Value)
                );
            }

            if (bufferSize == 2)
            {
                //shopkeeper_vr
                drawCall.IndiceType = DrawElementsType.UnsignedShort;
            }
            else if (bufferSize == 4)
            {
                //glados
                drawCall.IndiceType = DrawElementsType.UnsignedInt;
            }
            else
            {
                throw new Exception("Unsupported indice type");
            }

            var g = (KVObject)drawProperties["m_vertexBuffers"].Value;
            var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);
            vertexBuffer.Offset = Convert.ToUInt32(h.Properties["m_nBindOffsetBytes"].Value);
            drawCall.VertexBuffer = vertexBuffer;

            GL.GenVertexArrays(1, out drawCall.VertexArrayObject);

            GL.BindVertexArray(drawCall.VertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[drawCall.VertexBuffer.Id]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[drawCall.IndexBuffer.Id]);

            var curVertexBuffer = block.VertexBuffers[(int)drawCall.VertexBuffer.Id];
            var texCoordNum = 0;
            foreach (var attribute in curVertexBuffer.Attributes)
            {
                var attributeName = "v" + attribute.Name;

                // TODO: other params too?
                if (attribute.Name == "TEXCOORD" && texCoordNum++ > 0)
                {
                    attributeName += texCoordNum;
                }

                BindVertexAttrib(attribute, attributeName, drawCall.Shader, (int)curVertexBuffer.Size);
            }

            GL.BindVertexArray(0);

            return drawCall;
        }

        private void MeshControl_Paint(object sender, PaintEventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            ActiveCamera.Tick();

            //Animate light position
            var lightPos = ActiveCamera.Location;
            var cameraLeft = new Vector3((float)Math.Cos(ActiveCamera.Yaw + MathHelper.PiOver2), (float)Math.Sin(ActiveCamera.Yaw + MathHelper.PiOver2), 0);
            lightPos += cameraLeft * 200 * (float)Math.Sin(Environment.TickCount / 500.0);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            //var sw = System.Diagnostics.Stopwatch.StartNew();

            var prevShader = -1;
            var count = 0;

            foreach (var obj in MeshesToRender)
            {
                foreach (var call in obj.DrawCalls)
                {
                    if (call.Shader != prevShader)
                    {
                        prevShader = call.Shader;

                        //Bind shader
                        GL.UseProgram(call.Shader);

                        //Set shader uniforms
                        var projectionLoc = GL.GetUniformLocation(call.Shader, "projection");
                        GL.UniformMatrix4(projectionLoc, false, ref ActiveCamera.ProjectionMatrix);

                        var modelviewLoc = GL.GetUniformLocation(call.Shader, "modelview");
                        GL.UniformMatrix4(modelviewLoc, false, ref ActiveCamera.CameraViewMatrix);

                        var lightPosAttrib = GL.GetUniformLocation(call.Shader, "vLightPosition");
                        GL.Uniform3(lightPosAttrib, lightPos);

                        var eyePosAttrib = GL.GetUniformLocation(call.Shader, "vEyePosition");
                        GL.Uniform3(eyePosAttrib, ActiveCamera.Location);
                    }
                    else
                    {
                        count++;
                    }

                    var transform = obj.Transform;
                    var transformLoc = GL.GetUniformLocation(call.Shader, "transform");
                    GL.UniformMatrix4(transformLoc, false, ref transform);

                    //Bind VAO
                    GL.BindVertexArray(call.VertexArrayObject);

                    var textureUnit = 0;
                    foreach (var texture in call.Material.Textures)
                    {
                        TryToBindTexture(call.Shader, textureUnit++, texture.Key, texture.Value);
                    }

                    var uniformLocation = GL.GetUniformLocation(call.Shader, "m_vTintColorDrawCall");

                    if (uniformLocation > -1)
                    {
                        GL.Uniform3(uniformLocation, call.TintColor);
                    }

                    uniformLocation = GL.GetUniformLocation(call.Shader, "m_vTintColorSceneObject");

                    if (uniformLocation > -1)
                    {
                        GL.Uniform4(uniformLocation, obj.TintColor);
                    }

                    foreach (var param in call.Material.FloatParams)
                    {
                        uniformLocation = GL.GetUniformLocation(call.Shader, param.Key);

                        if (uniformLocation > -1)
                        {
                            GL.Uniform1(uniformLocation, param.Value);
                        }
                    }

                    foreach (var param in call.Material.VectorParams)
                    {
                        uniformLocation = GL.GetUniformLocation(call.Shader, param.Key);

                        if (uniformLocation > -1)
                        {
                            GL.Uniform4(uniformLocation, param.Value);
                        }
                    }

                    if (call.Material.IntParams.ContainsKey("F_ALPHA_TEST") && call.Material.IntParams["F_ALPHA_TEST"] == 1)
                    {
                        var alphaReference = GL.GetUniformLocation(call.Shader, "g_flAlphaTestReference");
                        GL.Uniform1(alphaReference, call.Material.FloatParams.ContainsKey("g_flAlphaTestReference") ? call.Material.FloatParams["g_flAlphaTestReference"] : 0f);
                    }
                    else
                    {
                        var alphaReference = GL.GetUniformLocation(call.Shader, "g_flAlphaTestReference");
                        GL.Uniform1(alphaReference, 0f);
                    }

                    /*
                    if (call.Material.IntParams.ContainsKey("F_TRANSLUCENT") && call.Material.IntParams["F_TRANSLUCENT"] == 1)
                    {
                        GL.Enable(EnableCap.Blend);
                        GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                    }
                    else
                    {
                        GL.Disable(EnableCap.Blend);
                    }
                    */

                    GL.DrawElements(call.PrimitiveType, call.IndexCount, call.IndiceType, (IntPtr)call.StartIndex);
                }
            }

            //Console.WriteLine(count + " saved");

            //sw.Stop(); Console.WriteLine("{0} {1}", sw.Elapsed, sw.ElapsedTicks);

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

        // TODO: we're taking boundaries of first scene
        private void LoadBoundingBox()
        {
            var yo = MeshesToRender.FirstOrDefault();
            if (yo == null)
            {
                return;
            }

            var data = (BinaryKV3)yo.Resource.Blocks[BlockType.DATA];
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
