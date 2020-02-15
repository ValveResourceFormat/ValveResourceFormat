using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using SteamDatabase.ValvePak;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using MathHelper = OpenTK.MathHelper;
using Matrix4 = OpenTK.Matrix4;
using Timer = System.Timers.Timer;
using Vector3 = OpenTK.Vector3;

namespace GUI.Types.Renderer
{
    public enum RenderSubject
    {
        Unknown,
        Model,
        World,
    }

    internal class Renderer : IDisposable
    {
        private readonly RenderSubject SubjectType;
        private readonly Stopwatch PreciseTimer;
        private readonly List<Tuple<string, Matrix4>> cameras;
        private readonly DebugUtil Debug;

        public MaterialLoader MaterialLoader { get; }
        public Package CurrentPackage { get; }
        public string CurrentFileName { get; }

        private readonly TabControl tabs;

        private readonly List<MeshObject> MeshesToRender;

        private readonly List<ValveResourceFormat.ResourceTypes.ModelAnimation.Animation> Animations;
        private Skeleton Skeleton;

        private bool Loaded;

        private GLControl meshControl;
        private Label cameraLabel;
        private Label fpsLabel;
        private ComboBox renderModeComboBox;
        private double previousFrameTime;
        private Timer InputTimer;

        private CheckedListBox animationBox;
        private CheckedListBox cameraBox;

        private Camera ActiveCamera;
        private Animation ActiveAnimation;

        private Vector3 MinBounds;
        private Vector3 MaxBounds;
        private Vector3 GlobalLight = Vector3.Zero;

        private int AnimationTexture;

        public Renderer(TabControl mainTabs, string fileName, Package currentPackage, RenderSubject subjectType = RenderSubject.Unknown)
        {
            SubjectType = subjectType;

            PreciseTimer = new Stopwatch();
            PreciseTimer.Start();

            MeshesToRender = new List<MeshObject>();
            Animations = new List<Animation>();
            cameras = new List<Tuple<string, Matrix4>>();

            CurrentPackage = currentPackage;
            CurrentFileName = fileName;
            tabs = mainTabs;

            Debug = new DebugUtil();

            Skeleton = new Skeleton(); // Default empty skeleton

            MaterialLoader = MaterialLoader.GetInstance(CurrentFileName, CurrentPackage);
        }

        public void Dispose()
        {
            Console.WriteLine("Disposing renderer");

            Loaded = false;

            InputTimer.Dispose();
            meshControl.Dispose();
            cameraLabel.Dispose();
            fpsLabel.Dispose();
            animationBox.Dispose();
            cameraBox.Dispose();

            if (renderModeComboBox != null)
            {
                renderModeComboBox.Dispose();
            }
        }

        public void AddMeshObject(MeshObject obj) => MeshesToRender.Add(obj);
        public void AddAnimations(List<ValveResourceFormat.ResourceTypes.ModelAnimation.Animation> animations) => Animations.AddRange(animations);
        public void SetSkeleton(Skeleton skeleton) => Skeleton = skeleton;

        public Control CreateGL()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
            };

            cameraLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                Dock = DockStyle.Top,
            };
            panel.Controls.Add(cameraLabel);

            fpsLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoSize = true,
                Dock = DockStyle.Top,
            };
            panel.Controls.Add(fpsLabel);

            var controlsPanel = new Panel
            {
                Dock = DockStyle.Left,
            };

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

            if (SubjectType == RenderSubject.Model)
            {
                renderModeComboBox = new ComboBox
                {
                    Dock = DockStyle.Top,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                };

                renderModeComboBox.SelectedIndexChanged += OnRenderModeChange;
                controlsPanel.Controls.Add(renderModeComboBox);
            }

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
            meshControl.VisibleChanged += MeshControl_GotFocus;
            meshControl.Disposed += MeshControl_Disposed;

            panel.Controls.Add(meshControl);
            return panel;
        }

        private void MeshControl_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void OnRenderModeChange(object sender, EventArgs e)
        {
            // Override placeholder item
            if (renderModeComboBox.SelectedIndex > 0)
            {
                renderModeComboBox.Items[0] = "Default";
            }

            // Rebuild shaders with updated parameters
            foreach (var obj in MeshesToRender)
            {
                foreach (var call in obj.DrawCalls)
                {
                    // Recycle old shader parameters that are not render modes since we are scrapping those anyway
                    var arguments = call.Shader.Parameters.List.Where(arg => !arg.ParameterName.StartsWith("renderMode")).ToList();
                    call.Shader.Parameters.List.Clear();
                    call.Shader.Parameters.List.AddRange(arguments);

                    // First item is default (or change render mode...) so don't add it
                    if (renderModeComboBox.SelectedIndex > 0)
                    {
                        call.Shader.Parameters.List.Add(new ArgumentDependencies.ArgumentDependency
                        {
                            ParameterName = $"renderMode_{renderModeComboBox.SelectedItem}",
                            Fingerprint = 1,
                        });
                    }

                    call.Shader = ShaderLoader.LoadShader(call.Shader.Name, call.Shader.Parameters);
                }
            }
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

                // Make a copy of the camera
                ActiveCamera = new Camera(cameraBox.Items[e.Index] as Camera);
                ActiveCamera.SetViewportSize(meshControl.Width, meshControl.Height);

                // Repaint
                meshControl.Update();
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

                ActiveAnimation = animationBox.Items[e.Index] as ValveResourceFormat.ResourceTypes.ModelAnimation.Animation;
            }
            else if (e.CurrentValue == CheckState.Checked && cameraBox.CheckedItems.Count == 1)
            {
                e.NewValue = CheckState.Checked;
            }
        }

        private void MeshControl_GotFocus(object sender, EventArgs e)
        {
            meshControl.MakeCurrent();
            GL.Flush();
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
            InputTimer = new Timer
            {
                Enabled = true,
                Interval = 1000 / 60,
            };
            InputTimer.Elapsed += InputTick;
            InputTimer.Start();
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

            // If no defaul camera was found, make one up
            if (ActiveCamera == null)
            {
                ActiveCamera = Camera.FromBoundingBox(MinBounds, MaxBounds);
            }

            cameraBox.Items.Add(ActiveCamera, true);

            // Set camera viewport size
            ActiveCamera.SetViewportSize(meshControl.Width, meshControl.Width);

            foreach (var cameraInfo in cameras)
            {
                var camera = new Camera(cameraInfo.Item2, cameraInfo.Item1);
                cameraBox.Items.Add(camera);
            }

            ActiveAnimation = Animations.Count > 0 ? Animations[0] : null;
            animationBox.Items.AddRange(Animations.ToArray());

            foreach (var obj in MeshesToRender)
            {
                obj.LoadFromResource(MaterialLoader);
            }

            // Gather render modes
            var renderModes = MeshesToRender
                .SelectMany(mesh => mesh.DrawCalls.SelectMany(drawCall => drawCall.Shader.RenderModes))
                .Distinct()
                .ToArray();

            if (SubjectType == RenderSubject.Model)
            {
                renderModeComboBox.Items.Clear();
                renderModeComboBox.Items.Add("Change render mode...");
                renderModeComboBox.Items.AddRange(renderModes);
                renderModeComboBox.SelectedIndex = 0;
            }

#if DEBUG
            Debug.Setup();
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
            //Console.WriteLine($"Adding Camera {name} with matrix {megaMatrix}");
            cameras.Add(new Tuple<string, Matrix4>(name, megaMatrix));
        }

        public void SetDefaultWorldCamera(Vector3 target)
        {
            // Do a little trigonometry, we want to look at our target from a distance of 1500 units at an angle of 70deg
            var distance = 2000;
            var angle = MathHelper.DegreesToRadians(60);
            var location = target + new Vector3(0, -distance * (float)Math.Cos(angle), distance * (float)Math.Sin(angle));
            var cameraMatrix = Matrix4.CreateRotationY(angle) * Matrix4.CreateRotationZ(MathHelper.PiOver2) * Matrix4.CreateTranslation(location);

            // Set camera
            ActiveCamera = new Camera(cameraMatrix, "worldspawn");
        }

        public void SetWorldGlobalLight(Vector3 position)
        {
            GlobalLight = position;
        }

        private void MeshControl_Paint(object sender, PaintEventArgs e)
        {
            if (!Loaded)
            {
                return;
            }

            var deltaTime = GetElapsedTime();

            // Tick camera
            ActiveCamera.Tick(deltaTime);

            // Update labels
            cameraLabel.Text = $"{ActiveCamera.Location.X}, {ActiveCamera.Location.Y}, {ActiveCamera.Location.Z}\n(yaw: {ActiveCamera.Yaw} pitch: {ActiveCamera.Pitch})";
            fpsLabel.Text = $"FPS: {Math.Round(1f / deltaTime)}";

            // Set light position
            Vector3 lightPos;

            if (GlobalLight == Vector3.Zero)
            {
                lightPos = ActiveCamera.Location;
                var cameraLeft = new Vector3((float)Math.Cos(ActiveCamera.Yaw + MathHelper.PiOver2), (float)Math.Sin(ActiveCamera.Yaw + MathHelper.PiOver2), 0);
                lightPos += cameraLeft;
            }
            else
            {
                lightPos = GlobalLight;
            }

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
                animationMatrices = ActiveAnimation.GetAnimationMatricesAsArray((float)PreciseTimer.Elapsed.TotalSeconds, Skeleton);
                //Update animation texture
                GL.BindTexture(TextureTarget.Texture2D, AnimationTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 4, Skeleton.Bones.Length, 0, PixelFormat.Rgba, PixelType.Float, animationMatrices);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var prevShader = -1;
            var prevMaterial = string.Empty;

            //var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var obj in MeshesToRender)
            {
                var objChanged = true;

                foreach (var call in obj.DrawCalls)
                {
                    int uniformLocation;
                    if (call.Shader.Program != prevShader)
                    {
                        objChanged = true;
                        prevShader = call.Shader.Program;

                        GL.UseProgram(call.Shader.Program);

                        uniformLocation = call.Shader.GetUniformLocation("vLightPosition");
                        GL.Uniform3(uniformLocation, lightPos);

                        uniformLocation = call.Shader.GetUniformLocation("vEyePosition");
                        GL.Uniform3(uniformLocation, ActiveCamera.Location);

                        uniformLocation = call.Shader.GetUniformLocation("projection");
                        var matrix = ActiveCamera.ProjectionMatrix;
                        GL.UniformMatrix4(uniformLocation, false, ref matrix);

                        uniformLocation = call.Shader.GetUniformLocation("modelview");
                        matrix = ActiveCamera.CameraViewMatrix;
                        GL.UniformMatrix4(uniformLocation, false, ref matrix);

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

                    if (call.Material.Parameters.Name != prevMaterial)
                    {
                        prevMaterial = call.Material.Parameters.Name;

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

                        foreach (var param in call.Material.Parameters.FloatParams)
                        {
                            uniformLocation = call.Shader.GetUniformLocation(param.Key);

                            if (uniformLocation > -1)
                            {
                                GL.Uniform1(uniformLocation, param.Value);
                            }
                        }

                        foreach (var param in call.Material.Parameters.VectorParams)
                        {
                            uniformLocation = call.Shader.GetUniformLocation(param.Key);

                            if (uniformLocation > -1)
                            {
                                GL.Uniform4(uniformLocation, new Vector4(param.Value.X, param.Value.Y, param.Value.Z, param.Value.W));
                            }
                        }

                        var alpha = 0f;
                        if (call.Material.Parameters.IntParams.ContainsKey("F_ALPHA_TEST") &&
                            call.Material.Parameters.IntParams["F_ALPHA_TEST"] == 1 &&
                            call.Material.Parameters.FloatParams.ContainsKey("g_flAlphaTestReference"))
                        {
                            alpha = call.Material.Parameters.FloatParams["g_flAlphaTestReference"];
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
            Debug.Reset();

            //DebugDrawSkeleton();

            Debug.Draw(ActiveCamera, false);
#endif

            meshControl.SwapBuffers();
            meshControl.Invalidate();
        }

        private void DebugDrawSkeleton()
        {
            if (ActiveAnimation != null)
            {
                var anim = ActiveAnimation.GetAnimationMatrices((float)PreciseTimer.Elapsed.TotalSeconds, Skeleton);

                void DebugDrawRecursive(Bone bone, System.Numerics.Matrix4x4 matrix)
                {
                    if (bone.Index >= 0)
                    {
                        var transform = bone.BindPose * matrix;
                        Debug.AddCube(transform * anim[bone.Index]);

                        foreach (var child in bone.Children)
                        {
                            DebugDrawRecursive(child, transform);
                        }
                    }
                    else
                    {
                        foreach (var child in bone.Children)
                        {
                            DebugDrawRecursive(child, matrix);
                        }
                    }
                }

                foreach (var root in Skeleton.Roots)
                {
                    DebugDrawRecursive(root, System.Numerics.Matrix4x4.Identity);
                }
            }
        }

        // TODO: we're taking boundaries of first scene
        private void LoadBoundingBox()
        {
            var yo = MeshesToRender.FirstOrDefault();
            if (yo == null)
            {
                return;
            }

            var data = (BinaryKV3)yo.Resource.DataBlock;
            var sceneObjects = data.Data.GetArray("m_sceneObjects");

            if (sceneObjects.Length == 0)
            {
                return;
            }

            var boundingBox = sceneObjects[0];
            var minBounds = boundingBox.GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = boundingBox.GetSubCollection("m_vMaxBounds").ToVector3();

            MinBounds = new Vector3(minBounds.X, minBounds.Y, minBounds.Z);
            MaxBounds = new Vector3(maxBounds.X, maxBounds.Y, maxBounds.Z);
        }

        // Get Elapsed time in seconds
        private float GetElapsedTime()
        {
            var timeslice = PreciseTimer.Elapsed.TotalSeconds;

            var diff = timeslice - previousFrameTime;

            previousFrameTime = timeslice;

            return (float)diff;
        }
    }
}
