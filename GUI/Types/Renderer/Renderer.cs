using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Renderer.Animation;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
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

        private readonly List<Animation.Animation> Animations;
        private Skeleton Skeleton;

        private bool Loaded;

        private GLControl meshControl;
        private Label cameraLabel;
        private Label fpsLabel;

        private CheckedListBox animationBox;
        private CheckedListBox cameraBox;
        private readonly List<Tuple<string, Matrix4>> cameras;

        private Camera ActiveCamera;
        private Animation.Animation ActiveAnimation;

        private Vector3 MinBounds;
        private Vector3 MaxBounds;

        private readonly DebugUtil Debug;

        private int AnimationTexture;

        public Renderer(TabControl mainTabs, string fileName, Package currentPackage)
        {
            MeshesToRender = new List<MeshObject>();
            Animations = new List<Animation.Animation>();
            cameras = new List<Tuple<string, Matrix4>>();

            CurrentPackage = currentPackage;
            CurrentFileName = fileName;
            tabs = mainTabs;

            Debug = new DebugUtil();

            Skeleton = new Skeleton(); // Default empty skeleton

            MaterialLoader = new MaterialLoader(CurrentFileName, CurrentPackage);
        }

        public void AddMeshObject(MeshObject obj)
        {
            MeshesToRender.Add(obj);
        }

        public void AddAnimations(List<Animation.Animation> animations)
        {
            Animations.AddRange(animations);
        }

        public void SetSkeleton(Skeleton skeleton)
        {
            Skeleton = skeleton;
        }

        public Control CreateGL()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;

            cameraLabel = new Label();
            cameraLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cameraLabel.AutoSize = true;
            cameraLabel.Dock = DockStyle.Top;
            panel.Controls.Add(cameraLabel);

            fpsLabel = new Label();
            fpsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            fpsLabel.AutoSize = true;
            fpsLabel.Dock = DockStyle.Top;
            panel.Controls.Add(fpsLabel);

            var controlsPanel = new Panel();
            controlsPanel.Dock = DockStyle.Left;

            animationBox = new CheckedListBox();
            animationBox.Width *= 2;
            animationBox.Dock = DockStyle.Fill;
            animationBox.ItemCheck += AnimationBox_ItemCheck;
            animationBox.CheckOnClick = true;
            controlsPanel.Controls.Add(animationBox);

            cameraBox = new CheckedListBox();
            cameraBox.Width *= 2;
            cameraBox.Dock = DockStyle.Top;
            cameraBox.ItemCheck += CameraBox_ItemCheck;
            cameraBox.CheckOnClick = true;
            controlsPanel.Controls.Add(cameraBox);

            panel.Controls.Add(controlsPanel);

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

            panel.Controls.Add(meshControl);
            return panel;
        }

        private void CameraBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            //https://social.msdn.microsoft.com/Forums/windows/en-US/5333cdf2-a669-467c-99ae-1530e91da43a/checkedlistbox-allow-only-one-item-to-be-selected?forum=winforms
            if (e.NewValue == CheckState.Checked)
            {
                for (var ix = 0; ix < cameraBox.Items.Count; ++ix)
                {
                    if (e.Index != ix)
                    {
                        cameraBox.ItemCheck -= CameraBox_ItemCheck;
                        cameraBox.SetItemChecked(ix, false);
                        cameraBox.ItemCheck += CameraBox_ItemCheck;
                    }
                }

                ActiveCamera = cameraBox.Items[e.Index] as Camera;
            }
            else if (e.CurrentValue == CheckState.Checked && cameraBox.CheckedItems.Count == 1)
            {
                e.NewValue = CheckState.Checked;
            }
        }

        private void AnimationBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            //https://social.msdn.microsoft.com/Forums/windows/en-US/5333cdf2-a669-467c-99ae-1530e91da43a/checkedlistbox-allow-only-one-item-to-be-selected?forum=winforms
            if (e.NewValue == CheckState.Checked)
            {
                for (var ix = 0; ix < animationBox.Items.Count; ++ix)
                {
                    if (e.Index != ix)
                    {
                        animationBox.ItemCheck -= AnimationBox_ItemCheck;
                        animationBox.SetItemChecked(ix, false);
                        animationBox.ItemCheck += AnimationBox_ItemCheck;
                    }
                }

                ActiveAnimation = animationBox.Items[e.Index] as Animation.Animation;
            }
            else if (e.CurrentValue == CheckState.Checked && cameraBox.CheckedItems.Count == 1)
            {
                e.NewValue = CheckState.Checked;
            }
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

        private void CheckOpenGL()
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
                MaterialLoader.MaxTextureMaxAnisotropy = GL.GetInteger((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
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

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            GL.ClearColor(Settings.BackgroundColor);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            InitializeInputTick();

            ActiveCamera = new Camera(tabs.Width, tabs.Height, MinBounds, MaxBounds);
            cameraBox.Items.Add(ActiveCamera, true);

            foreach (var cameraInfo in cameras)
            {
                var camera = new Camera(tabs.Width, tabs.Height, cameraInfo.Item2, cameraInfo.Item1);
                cameraBox.Items.Add(camera);
            }

            ActiveAnimation = Animations.Count > 0 ? Animations[0] : null;
            animationBox.Items.AddRange(Animations.ToArray());

            foreach (var obj in MeshesToRender)
            {
                obj.LoadFromResource(MaterialLoader);
            }

#if DEBUG
            Debug.Setup();
            //Skeleton.DebugDraw(Debug);
#endif

            // Create animation texture
            AnimationTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, AnimationTexture);
            // Set clamping to edges
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            // Set nearest-neighbor sampling since we don't want to interpolate matrix rows
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            //Unbind texture again
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // TODO: poor hack
            FileExtensions.ClearCache();

            Loaded = true;

            Console.WriteLine("{0} draw calls total", MeshesToRender.Sum(x => x.DrawCalls.Count));
        }

        public void AddCamera(string name, Matrix4 megaMatrix)
        {
            Console.WriteLine($"Adding Camera {name} with matrix {megaMatrix}");
            cameras.Add(new Tuple<string, Matrix4>(name, megaMatrix));
        }

        private void MeshControl_Paint(object sender, PaintEventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            var fps = fpsLabel.Text;
            ActiveCamera.Tick(ref fps);
            fpsLabel.Text = fps;

            cameraLabel.Text = $"{ActiveCamera.Location.X}, {ActiveCamera.Location.Y}, {ActiveCamera.Location.Z}\n(yaw: {ActiveCamera.Yaw})";

            //Animate light position
            var lightPos = ActiveCamera.Location;
            var cameraLeft = new Vector3((float)Math.Cos(ActiveCamera.Yaw + MathHelper.PiOver2), (float)Math.Sin(ActiveCamera.Yaw + MathHelper.PiOver2), 0);
            lightPos += cameraLeft * 200 * (float)Math.Sin(Environment.TickCount / 500.0);

            // Get animation matrices
            var animationMatrices = new float[Skeleton.Bones.Length * 16];
            for (var i = 0; i < Skeleton.Bones.Length; i++)
            {
                // Default to identity matrices
                animationMatrices[i * 16] = 1.0f;
                animationMatrices[(i * 16) + 5] = 1.0f;
                animationMatrices[(i * 16) + 10] = 1.0f;
                animationMatrices[(i * 16) + 15] = 1.0f;
            }

            if (Animations.Count > 0)
            {
                animationMatrices = ActiveAnimation.GetAnimationMatricesAsArray(Environment.TickCount / 1000.0f, Skeleton);
                //Update animation texture
                GL.BindTexture(TextureTarget.Texture2D, AnimationTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 4, Skeleton.Bones.Length, 0, PixelFormat.Rgba, PixelType.Float, animationMatrices);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var prevShader = -1;
            var prevMaterial = string.Empty;
            var objChanged = false;
            int uniformLocation;

            //var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var obj in MeshesToRender)
            {
                objChanged = true;

                foreach (var call in obj.DrawCalls)
                {
                    if (call.Shader.Program != prevShader)
                    {
                        objChanged = true;
                        prevShader = call.Shader.Program;

                        GL.UseProgram(call.Shader.Program);

                        uniformLocation = call.Shader.GetUniformLocation("projection");
                        GL.UniformMatrix4(uniformLocation, false, ref ActiveCamera.ProjectionMatrix);

                        uniformLocation = call.Shader.GetUniformLocation("modelview");
                        GL.UniformMatrix4(uniformLocation, false, ref ActiveCamera.CameraViewMatrix);

                        uniformLocation = call.Shader.GetUniformLocation("vLightPosition");
                        GL.Uniform3(uniformLocation, lightPos);

                        uniformLocation = call.Shader.GetUniformLocation("vEyePosition");
                        GL.Uniform3(uniformLocation, ActiveCamera.Location);

                        uniformLocation = call.Shader.GetUniformLocation("bAnimated");
                        if (uniformLocation != -1)
                        {
                            GL.Uniform1(uniformLocation, Animations.Count == 0 ? 0.0f : 1.0f);
                        }

                        //Push animation texture to the shader (if it supports it)
                        if (Animations.Count > 0)
                        {
                            uniformLocation = call.Shader.GetUniformLocation("animationTexture");
                            if (uniformLocation != -1)
                            {
                                GL.ActiveTexture(TextureUnit.Texture0);
                                GL.BindTexture(TextureTarget.Texture2D, AnimationTexture);
                                GL.Uniform1(uniformLocation, 0);
                            }

                            uniformLocation = call.Shader.GetUniformLocation("fNumBones");
                            var uniformLocation2 = GL.GetUniformLocation(call.Shader.Program,"fNumBones");
                            if (uniformLocation != -1)
                            {
                                var v = (float)Math.Max(1, Skeleton.Bones.Length - 1);
                                GL.Uniform1(uniformLocation, v);
                            }
                        }
                    }

                    // Stupidly hacky
                    if (objChanged)
                    {
                        objChanged = false;
                        prevMaterial = string.Empty;

                        var transform = obj.Transform;
                        uniformLocation = call.Shader.GetUniformLocation("transform");
                        GL.UniformMatrix4(uniformLocation, false, ref transform);

                        uniformLocation = call.Shader.GetUniformLocation("m_vTintColorSceneObject");
                        if (uniformLocation > -1)
                        {
                            GL.Uniform4(uniformLocation, obj.TintColor);
                        }
                    }

                    GL.BindVertexArray(call.VertexArrayObject);

                    uniformLocation = call.Shader.GetUniformLocation("m_vTintColorDrawCall");
                    if (uniformLocation > -1)
                    {
                        GL.Uniform3(uniformLocation, call.TintColor);
                    }

                    if (call.Material.Name != prevMaterial)
                    {
                        prevMaterial = call.Material.Name;

                        //Start at 1, texture unit 0 is reserved for the animation texture
                        var textureUnit = 1;
                        foreach (var texture in call.Material.Textures)
                        {
                            uniformLocation = call.Shader.GetUniformLocation(texture.Key);

                            if (uniformLocation > -1)
                            {
                                GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
                                GL.BindTexture(TextureTarget.Texture2D, texture.Value);
                                GL.Uniform1(uniformLocation, textureUnit);

                                textureUnit++;
                            }
                        }

                        foreach (var param in call.Material.FloatParams)
                        {
                            uniformLocation = call.Shader.GetUniformLocation(param.Key);

                            if (uniformLocation > -1)
                            {
                                GL.Uniform1(uniformLocation, param.Value);
                            }
                        }

                        foreach (var param in call.Material.VectorParams)
                        {
                            uniformLocation = call.Shader.GetUniformLocation(param.Key);

                            if (uniformLocation > -1)
                            {
                                GL.Uniform4(uniformLocation, param.Value);
                            }
                        }

                        var alpha = 0f;
                        if (call.Material.IntParams.ContainsKey("F_ALPHA_TEST") &&
                            call.Material.IntParams["F_ALPHA_TEST"] == 1 &&
                            call.Material.FloatParams.ContainsKey("g_flAlphaTestReference"))
                        {
                            alpha = call.Material.FloatParams["g_flAlphaTestReference"];
                        }

                        var alphaReference = call.Shader.GetUniformLocation("g_flAlphaTestReference");
                        GL.Uniform1(alphaReference, alpha);

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
                    }

                    GL.DrawElements(call.PrimitiveType, call.IndexCount, call.IndiceType, (IntPtr)call.StartIndex);
                }
            }

            //sw.Stop(); Console.WriteLine("{0} {1}", sw.Elapsed, sw.ElapsedTicks);

            // Only needed when debugging if something doesnt work, causes high CPU
            /*
            var error = GL.GetError();

            if (error != ErrorCode.NoError)
            {
                Console.WriteLine(error);
            }
            */

#if DEBUG
            Debug.Draw(ActiveCamera, false);
#endif

            meshControl.SwapBuffers();
            meshControl.Invalidate();
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
    }
}
