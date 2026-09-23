using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    /// <summary>
    /// A 3D sky is placed rigidly and drawn through a second camera that applies the scale. These check
    /// that this is equivalent to scaling the sky into the world: for positions, projection and fog.
    /// </summary>
    public class SkyTransformTest
    {
        private const float Tolerance = 1e-3f;

        /// <summary>Both sides go through different matrix products, so the error grows with the value.</summary>
        private static float ToleranceFor(float expected) => MathF.Max(Tolerance, MathF.Abs(expected) * 2e-4f);

        /// <summary>The sky camera position in the sky map, before the reference is applied.</summary>
        private static readonly Vector3 SkyCameraOrigin = new(-96f, 48f, 64f);

        private static readonly float[] Scales = [1f, 16f];

        private static readonly Matrix4x4[] References =
        [
            Matrix4x4.Identity,
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(0f, 35f, 0f), new Vector3(1024f, -512f, 96f)),
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(12f, -80f, 25f), new Vector3(-256f, 320f, -64f)),
        ];

        private static readonly Vector3[] SkyPoints =
        [
            new(0f, 0f, 0f),
            new(512f, -256f, 128f),
            new(-1024f, 2048f, -320f),
            new(64f, 64f, 900f),
        ];

        private readonly List<IDisposable> harnessContexts = [];

        [After(HookType.Test)]
        public void DisposeHarnessContexts()
        {
            for (var i = harnessContexts.Count - 1; i >= 0; i--)
            {
                harnessContexts[i].Dispose();
            }

            harnessContexts.Clear();
        }

        private static SkyTransform CreateSkybox(Matrix4x4 reference, float scale)
        {
            // The loader applies the reference to the whole sky map, the sky camera included
            var origin = Vector3.Transform(SkyCameraOrigin, reference);

            return new SkyTransform(reference.Translation, origin, scale);
        }

        private Scene CreateScene()
        {
            var fileLoader = new GameFileLoader(null, null);
            harnessContexts.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            harnessContexts.Add(context);

            var scene = new Scene(context);
            harnessContexts.Add(scene);

            return scene;
        }

        private static Vector3 ToNdc(Vector3 position, in Matrix4x4 viewProjection)
        {
            var clip = Vector4.Transform(new Vector4(position, 1f), viewProjection);

            return clip.AsVector3() / clip.W;
        }

        private static async Task AssertClose(Vector3 actual, Vector3 expected, string because)
        {
            await Assert.That(actual.X).IsEqualTo(expected.X).Within(ToleranceFor(expected.X)).Because($"X for {because}");
            await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(ToleranceFor(expected.Y)).Because($"Y for {because}");
            await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(ToleranceFor(expected.Z)).Because($"Z for {because}");
        }

        private static Camera CreateMainCamera(float farPlane)
        {
            var camera = new Camera(90f);
            camera.SetViewportSize(1920, 1080);
            camera.SetLocationPitchYaw(new Vector3(320f, -180f, 72f), float.DegreesToRadians(-10f), float.DegreesToRadians(25f));
            camera.FarPlane = farPlane;
            camera.CreateProjectionMatrix();
            camera.RecalculateMatrices();

            return camera;
        }

        /// <summary>
        /// A sky point maps to where scaling the sky about its camera and then applying the reference
        /// would put it.
        /// </summary>
        [Test]
        public async Task ToWorldLandsWhereScalingTheSkyWould()
        {
            foreach (var reference in References)
            {
                foreach (var scale in Scales)
                {
                    var skybox = CreateSkybox(reference, scale);

                    foreach (var authored in SkyPoints)
                    {
                        var placed = Vector3.Transform(authored, reference);
                        var scaled = Vector3.Transform((authored - SkyCameraOrigin) * scale, reference);

                        await AssertClose(skybox.ToWorld(placed), scaled, $"{authored} at {scale}x");
                    }
                }
            }
        }

        /// <summary>The sky camera stands where the main one does, seen from sky space.</summary>
        [Test]
        public async Task ToSkyUndoesToWorld()
        {
            foreach (var reference in References)
            {
                foreach (var scale in Scales)
                {
                    var skybox = CreateSkybox(reference, scale);

                    foreach (var authored in SkyPoints)
                    {
                        var placed = Vector3.Transform(authored, reference);

                        await AssertClose(skybox.ToSky(skybox.ToWorld(placed)), placed, $"{authored} at {scale}x");
                    }
                }
            }
        }

        /// <summary>A scale that is not positive has no sky to draw.</summary>
        [Test]
        public async Task RejectsScaleThatIsNotPositive()
        {
            await Assert.That(() => new SkyTransform(Vector3.Zero, Vector3.Zero, 0f)).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new SkyTransform(Vector3.Zero, Vector3.Zero, -16f)).Throws<ArgumentOutOfRangeException>();
        }

        /// <summary>
        /// The sky drawn through its own camera lands on the same pixels and at the same depth as the sky
        /// scaled into the world and drawn through the main camera. The depth matters for the sky depth
        /// range and the shared depth pyramid.
        /// </summary>
        [Test]
        public async Task SkyCameraProjectsLikeTheScaledSkyDoes()
        {
            foreach (var farPlane in new[] { float.PositiveInfinity, 12000f })
            {
                foreach (var reference in References)
                {
                    foreach (var scale in Scales)
                    {
                        var skybox = CreateSkybox(reference, scale);
                        var main = CreateMainCamera(farPlane);
                        var sky = new Camera();

                        skybox.ConfigureCamera(sky, main);

                        foreach (var authored in SkyPoints)
                        {
                            var placed = Vector3.Transform(authored, reference);

                            var inSky = ToNdc(placed, sky.ViewProjectionMatrix);
                            var inWorld = ToNdc(skybox.ToWorld(placed), main.ViewProjectionMatrix);

                            await AssertClose(inSky, inWorld, $"{authored} at {scale}x, far {farPlane}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gradient fog is authored in world units, so it has to give the same result from the distance
        /// and height the sky camera measures.
        /// </summary>
        [Test]
        public async Task GradientFogReadsTheSameThroughTheSkyCamera()
        {
            var skybox = CreateSkybox(References[1], 16f);

            var fog = new SceneGradientFog(CreateScene())
            {
                StartDist = 500f,
                EndDist = 6000f,
                HeightStart = 128f,
                HeightEnd = 2048f,
            };

            var world = fog.GetBiasAndScale(FogSpace.World);
            var inSky = fog.GetBiasAndScale(skybox.FogSpace);

            var worldCulling = fog.CullingParams(FogSpace.World);
            var skyCulling = fog.CullingParams(skybox.FogSpace);

            Matrix4x4.Invert(skybox.SkyToWorld, out var worldToSky);

            foreach (var distance in new[] { 0f, 500f, 3000f, 9000f })
            {
                var skyDistance = distance / skybox.Scale;

                await Assert.That(inSky.X + skyDistance * inSky.Z)
                    .IsEqualTo(world.X + distance * world.Z).Within(Tolerance).Because($"fog at {distance}");
            }

            foreach (var height in new[] { -200f, 0f, 512f, 4000f })
            {
                var skyHeight = Vector3.Transform(new Vector3(0f, 0f, height), worldToSky).Z;

                await Assert.That(inSky.Y + skyHeight * inSky.W)
                    .IsEqualTo(world.Y + height * world.W).Within(Tolerance).Because($"height fog at {height}");
            }

            await Assert.That(MathF.Sqrt(skyCulling.X) * skybox.Scale).IsEqualTo(MathF.Sqrt(worldCulling.X)).Within(Tolerance);
            await Assert.That(Vector3.Transform(new Vector3(0f, 0f, worldCulling.Y), worldToSky).Z)
                .IsEqualTo(skyCulling.Y).Within(Tolerance);
        }

        /// <summary>The same for the cubemap fog.</summary>
        [Test]
        public async Task CubemapFogReadsTheSameThroughTheSkyCamera()
        {
            var skybox = CreateSkybox(References[2], 16f);

            var fog = new SceneCubemapFog(CreateScene())
            {
                StartDist = 1200f,
                EndDist = 15000f,
                HeightStart = 64f,
                HeightEnd = 3000f,
                UseHeightFog = true,
            };

            var world = fog.OffsetScaleBiasExponent(FogSpace.World);
            var inSky = fog.OffsetScaleBiasExponent(skybox.FogSpace);

            foreach (var distance in new[] { 0f, 1200f, 7000f, 20000f })
            {
                var skyDistance = distance / skybox.Scale;

                await Assert.That(inSky.X + skyDistance * inSky.Y)
                    .IsEqualTo(world.X + distance * world.Y).Within(Tolerance).Because($"cube fog at {distance}");
            }

            Matrix4x4.Invert(skybox.SkyToWorld, out var worldToSky);

            var worldCulling = fog.CullingParams_Opacity(FogSpace.World);
            var skyCulling = fog.CullingParams_Opacity(skybox.FogSpace);

            await Assert.That(MathF.Sqrt(skyCulling.X) * skybox.Scale).IsEqualTo(MathF.Sqrt(worldCulling.X)).Within(Tolerance);
            await Assert.That(Vector3.Transform(new Vector3(0f, 0f, worldCulling.Y), worldToSky).Z)
                .IsEqualTo(skyCulling.Y).Within(Tolerance);
            await Assert.That(skyCulling.W).IsEqualTo(worldCulling.W);
        }
    }
}
