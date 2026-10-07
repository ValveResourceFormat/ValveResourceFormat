using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.Renderer.Utils.HammerEntities;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Where a helper line is drawn: solid where the scene does not hide it, and over the scene in its color's alpha.
    /// </summary>
    [Flags]
    internal enum HelperPasses
    {
        /// <summary>Opaque, where the scene does not hide it.</summary>
        Visible = 1,

        /// <summary>Blended with its alpha, over the scene.</summary>
        Overlay = 2,

        /// <summary>Both.</summary>
        Both = Visible | Overlay,
    }

    /// <summary>
    /// The vertices of helper shapes, split by how they are drawn: lines and triangles, each either where the scene
    /// does not hide them or over it.
    /// </summary>
    internal sealed class HelperVertices
    {
        /// <summary>Gets the lines drawn opaque where the scene does not hide them.</summary>
        public List<SimpleVertex> Lines { get; } = [];

        /// <summary>Gets the lines blended over the scene.</summary>
        public List<SimpleVertex> OverlayLines { get; } = [];

        /// <summary>Gets the triangles blended where the scene does not hide them.</summary>
        public List<SimpleVertex> Faces { get; } = [];

        /// <summary>Gets the triangles blended over the scene.</summary>
        public List<SimpleVertex> OverlayFaces { get; } = [];

        /// <summary>Gets the number of vertices in all the lists.</summary>
        public int Count => Lines.Count + OverlayLines.Count + Faces.Count + OverlayFaces.Count;

        /// <summary>Adds a line to the passes it is drawn in, opaque where the scene does not hide it.</summary>
        public void AddLine(Vector3 start, Vector3 end, Color32 color, HelperPasses passes)
        {
            if (passes.HasFlag(HelperPasses.Visible))
            {
                ShapeSceneNode.AddLine(Lines, start, end, color with { A = 255 });
            }

            if (passes.HasFlag(HelperPasses.Overlay))
            {
                ShapeSceneNode.AddLine(OverlayLines, start, end, color);
            }
        }

        /// <summary>Empties all the lists.</summary>
        public void Clear()
        {
            Lines.Clear();
            OverlayLines.Clear();
            Faces.Clear();
            OverlayFaces.Clear();
        }
    }

    /// <summary>
    /// Draws the shapes Hammer shows for a selected entity, as its class declares them in <see cref="HammerEntities"/>:
    /// volume boxes, radius spheres, direction arrows, lines to points, and the frustum, lobes and cones of lights.
    /// Shapes are built in the entity's space, X forward, and transformed into line vertices.
    /// </summary>
    internal readonly struct EntityHelperLines(HelperVertices output, Matrix4x4 transform, HelperPasses passes)
    {
        // A light's shapes are solid where seen, and faint over the scene
        private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
        private static readonly Color32 SoftEdgeColor = new(0, 255, 255, 32);
        private static readonly Color32 SkirtNearColor = new(255, 0, 64, 24);
        private static readonly Color32 SkirtColor = new(255, 64, 0, 24);
        private static readonly Color32 ShapeBoundsColor = new(255, 255, 0, 16);

        // Selected boxes take the editor's selection color, which is a user setting; this is Hammer's default
        private static readonly Color32 SelectionColor = new(255, 0, 0);

        // A box's edges are solid where seen and a quarter opaque over the scene, and its faces are tinted
        private const byte BoxOverlayAlpha = 64;
        private const byte BoxFaceAlpha = 32;
        private const byte BoxOverlayFaceAlpha = 8;
        private static readonly Color32 InnerConeNearColor = new(255, 0, 0);
        private static readonly Color32 InnerConeFarColor = new(255, 128, 0);
        private static readonly Color32 OuterConeNearColor = new(0, 0, 255);
        private static readonly Color32 OuterConeFarColor = new(0, 255, 255);

        /// <summary>Adds the helper shapes of an entity, if its class has any.</summary>
        /// <param name="output">Vertices to append to.</param>
        /// <param name="classname">The entity's classname.</param>
        /// <param name="entity">The entity keyvalues.</param>
        /// <param name="instance">The spawned entity, for the light it casts and the entities a class draws along with it.</param>
        /// <param name="placement">The entity's origin and angles, without scale.</param>
        /// <param name="toWorld">Transform from the entity's scene to the viewer's world.</param>
        /// <returns><see langword="true"/> when anything was drawn.</returns>
        public static bool TryAdd(HelperVertices output, string? classname, EntityLump.Entity entity, BaseEntity? instance, in Matrix4x4 placement, in Matrix4x4 toWorld)
        {
            if (classname == null || Get(classname) is not { } hammerEntity)
            {
                return false;
            }

            var count = output.Count;
            var alignedPlacement = Matrix4x4.CreateTranslation(placement.Translation);
            var oriented = new EntityHelperLines(output, placement * toWorld, HelperPasses.Both);

            // Spheres stay aligned to the world axes
            var aligned = new EntityHelperLines(output, alignedPlacement * toWorld, HelperPasses.Visible);

            foreach (var box in hammerEntity.Boxes)
            {
                var boxTransform = box.Space switch
                {
                    HammerEntity.BoxSpace.Oriented => placement,
                    HammerEntity.BoxSpace.WorldAligned => alignedPlacement,
                    _ => Matrix4x4.Identity,
                };

                new EntityHelperLines(output, boxTransform * toWorld, HelperPasses.Both).AddVolumeBox(box, entity);
            }

            foreach (var sphere in hammerEntity.Spheres)
            {
                aligned.AddSphere(sphere, entity);
            }

            // Lines to points in the entity's space, unless the point is the origin itself
            foreach (var line in hammerEntity.OffsetLines)
            {
                if (entity.GetVector3Property(line.Key) is var point && point != Vector3.Zero)
                {
                    oriented.AddLine(Vector3.Zero, point, line.Color);
                }
            }

            if (hammerEntity.Direction is { } direction)
            {
                aligned.With(HelperPasses.Overlay).AddDirectionArrow(GetDirection(direction, entity, placement));
            }

            // The light as it is cast, which follows its inputs
            if (instance is LightEntity { Light: { } light })
            {
                if (hammerEntity.Shapes.HasFlag(HammerEntity.Shape.BarnLight))
                {
                    oriented.AddBarnLight(light);
                }

                if (hammerEntity.Shapes.HasFlag(HammerEntity.Shape.RectLight))
                {
                    oriented.AddRectLight(light);
                }

                if (hammerEntity.Shapes.HasFlag(HammerEntity.Shape.OmniLight))
                {
                    oriented.AddOmni2Light(light);
                }

                if (hammerEntity.Shapes.HasFlag(HammerEntity.Shape.LightCone))
                {
                    oriented.With(HelperPasses.Visible).AddLightCone(light, entity.GetFloatProperty("falloff", 1f));
                }
            }

            if (hammerEntity.Shapes.HasFlag(HammerEntity.Shape.VolumetricFogController))
            {
                // The volume the fog volumes cover, which the controller is compiled with
                var bounds = new AABB(entity.GetVector3Property("box_mins"), entity.GetVector3Property("box_maxs"));
                aligned.With(HelperPasses.Overlay).AddBox(bounds, SelectionColor);

                if (instance != null)
                {
                    AddFogVolumes(output, instance);
                }
            }

            return output.Count > count;
        }

        // A fog controller shows the volumes of all the fog volumes it covers
        private static void AddFogVolumes(HelperVertices output, BaseEntity instance)
        {
            foreach (var volume in instance.EntitySystem.Entities)
            {
                if (volume.IsRemoved || volume.Classname != "env_volumetric_fog_volume" || volume.SpawnData is not { } data)
                {
                    continue;
                }

                var boxTransform = Matrix4x4.CreateTranslation(volume.WorldOrigin) * volume.Scene.ToViewerWorld;
                var bounds = new AABB(data.GetVector3Property("box_mins"), data.GetVector3Property("box_maxs"));

                new EntityHelperLines(output, boxTransform, HelperPasses.Overlay).AddBox(bounds, Color32.White);
            }
        }

        // The angles in a key, turned with the entity when they are local to it, or the entity's own forward
        private static Vector3 GetDirection(HammerEntity.DirectionArrow arrow, EntityLump.Entity entity, in Matrix4x4 placement)
        {
            if (arrow.Key == null)
            {
                return placement.GetRow(0).AsVector3();
            }

            var direction = EntityTransformHelper.EulerAnglesToForwardDirection(entity.GetVector3Property(arrow.Key));
            var isLocal = arrow.IsLocalKey != null && entity.ContainsKey(arrow.IsLocalKey)
                ? entity.GetBooleanProperty(arrow.IsLocalKey)
                : arrow.IsLocal;

            return isLocal ? Vector3.TransformNormal(direction, placement) : direction;
        }

        private void AddVolumeBox(HammerEntity.Box box, EntityLump.Entity entity)
        {
            var bounds = box.SizeKey != null
                ? AABB.FromCenteredSize(entity.GetVector3Property(box.SizeKey))
                : new AABB(entity.GetVector3Property(box.MinsKey!), entity.GetVector3Property(box.MaxsKey!));

            // A wire box is only its edges, solid where seen
            if (box.Wire)
            {
                With(HelperPasses.Visible).AddBox(bounds, SelectionColor);
                return;
            }

            var color = SelectionColor with { A = BoxOverlayAlpha };

            AddBoxFaces(bounds, SelectionColor);
            AddBox(bounds, color);

            // Where the influence starts fading out towards the sides
            var edgeFade = box.EdgeFadesKey != null ? entity.GetVector3Property(box.EdgeFadesKey) : Vector3.Zero;

            if (edgeFade == Vector3.Zero && box.EdgeFadeKey != null)
            {
                edgeFade = new Vector3(entity.GetFloatProperty(box.EdgeFadeKey));
            }

            if (edgeFade != Vector3.Zero)
            {
                var inner = new AABB(Vector3.Min(bounds.Min + edgeFade, bounds.Center), Vector3.Max(bounds.Max - edgeFade, bounds.Center));
                With(HelperPasses.Overlay).AddBox(inner, color);
            }
        }

        private void AddSphere(HammerEntity.Sphere sphere, EntityLump.Entity entity)
        {
            var radius = entity.GetFloatProperty(sphere.RadiusKey);

            if (!sphere.Lean)
            {
                AddLatLongSphere(radius, sphere.Color);
                return;
            }

            AddGreatCircles(radius, sphere.Color);

            // The edge fade is drawn as a fainter inner sphere
            if (sphere.EdgeFadeKey != null && entity.GetFloatProperty(sphere.EdgeFadeKey) is var edgeFade && edgeFade > 0f)
            {
                AddGreatCircles(MathF.Max(radius - edgeFade, 0f), sphere.Color with { A = 127 });
            }
        }

        // An arrow 48 units long along the direction, over short forward, right and up axes
        private void AddDirectionArrow(Vector3 direction)
        {
            const int Segments = 16;
            const float AxisLength = 16f;
            const float ShaftLength = 32f;
            const float HeadLength = 16f;
            const float HeadRadius = 4f;

            var rotation = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);
            var tip = Vector3.Transform(Vector3.UnitX * (ShaftLength + HeadLength), rotation);
            var previous = Vector3.Zero;

            AddLine(Vector3.Zero, Vector3.Transform(Vector3.UnitX * ShaftLength, rotation), Color32.White);

            AddLine(Vector3.Zero, Vector3.Transform(Vector3.UnitX * AxisLength, rotation), Color32.Red);
            AddLine(Vector3.Zero, Vector3.Transform(-Vector3.UnitY * AxisLength, rotation), Color32.Green);
            AddLine(Vector3.Zero, Vector3.Transform(Vector3.UnitZ * AxisLength, rotation), Color32.Blue);

            for (var i = 0; i <= Segments; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / Segments);
                var rim = Vector3.Transform(new Vector3(ShaftLength, sin * HeadRadius, cos * HeadRadius), rotation);

                if (i > 0)
                {
                    AddLine(previous, rim, Color32.White);
                    AddLine(rim, tip, Color32.White);
                }

                previous = rim;
            }
        }

        // Corner i takes the max along X, Y and Z where bits 0, 1 and 2 of i are set
        private static void GetCorners(in AABB box, Span<Vector3> corners)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = new Vector3(
                    (i & 1) != 0 ? box.Max.X : box.Min.X,
                    (i & 2) != 0 ? box.Max.Y : box.Min.Y,
                    (i & 4) != 0 ? box.Max.Z : box.Min.Z);
            }
        }

        private void AddBox(in AABB box, Color32 color)
        {
            Span<Vector3> corners = stackalloc Vector3[8];
            GetCorners(box, corners);

            // Each edge joins two corners that differ along one axis
            for (var i = 0; i < corners.Length; i++)
            {
                for (var axis = 1; axis < corners.Length; axis <<= 1)
                {
                    if ((i & axis) == 0)
                    {
                        AddLine(corners[i], corners[i | axis], color);
                    }
                }
            }
        }

        private void AddBarnLight(SceneLight light)
        {
            var range = MathF.Max(light.Range, 1f);
            var size = light.SizeParams;
            var skirt = MathUtils.Saturate(light.FallOff);
            var skirtNear = MathUtils.Saturate(light.SkirtNear);

            // A cookie decides the shape, which is then outlined as a rectangle
            var hasCookie = light.CookieTexturePath != null;
            var exponent = hasCookie ? 0f : SceneLight.GetShapeExponent(light.Shape);

            var nearHalfSize = new Vector2(size.X, size.Y);
            var farCenter = new Vector3(range, light.Shear.X, light.Shear.Y);
            var farHalfSize = BarnHalfSizeAt(nearHalfSize, size.Z, range);

            if (!hasCookie)
            {
                var soft = Vector2.Clamp(Vector2.One - new Vector2(light.SoftX, light.SoftY), Vector2.Zero, Vector2.One);

                AddBarnFrustum(nearHalfSize * soft, farCenter, farHalfSize * soft, exponent, SoftEdgeColor);
            }

            AddBarnFrustum(nearHalfSize, farCenter, farHalfSize, exponent, ShapeColor);

            if (skirtNear > 0f)
            {
                var center = farCenter * skirtNear;
                var halfSize = BarnHalfSizeAt(nearHalfSize, size.Z, range * skirtNear);

                AddSuperellipse(center, halfSize, exponent, SkirtNearColor);
                AddSuperellipseConnectors(Vector3.Zero, nearHalfSize, center, halfSize, exponent, 0f, 20f, SkirtNearColor);
            }

            if (skirt > 0f)
            {
                var center = farCenter * (1f - skirt);
                var halfSize = BarnHalfSizeAt(nearHalfSize, size.Z, range * (1f - skirt));

                AddSuperellipse(center, halfSize, exponent, SkirtColor);
                AddSuperellipseConnectors(center, halfSize, farCenter, farHalfSize, exponent, 10f, 20f, SkirtColor);
            }

            // A rounded shape also shows the rectangle it fits in
            if (exponent > 0f)
            {
                var bounds = With(HelperPasses.Overlay);
                bounds.AddSuperellipse(Vector3.Zero, nearHalfSize, 0f, ShapeBoundsColor);
                bounds.AddSuperellipse(farCenter, farHalfSize, 0f, ShapeBoundsColor);
            }
        }

        // A perspective barn light widens by size_params.z per unit of distance from its luminaire
        private static Vector2 BarnHalfSizeAt(Vector2 nearHalfSize, float widening, float distance)
            => nearHalfSize * (distance * widening + 1f);

        private void AddBarnFrustum(Vector2 nearHalfSize, Vector3 farCenter, Vector2 farHalfSize, float exponent, Color32 color)
        {
            AddSuperellipse(Vector3.Zero, nearHalfSize, exponent, color);
            AddSuperellipse(farCenter, farHalfSize, exponent, color);
            AddSuperellipseConnectors(Vector3.Zero, nearHalfSize, farCenter, farHalfSize, exponent, 45f, 90f, color);
        }

        private void AddRectLight(SceneLight light)
        {
            var range = MathF.Max(light.Range, 1f);
            var skirt = MathUtils.Saturate(light.FallOff);

            AddSuperellipse(Vector3.Zero, new Vector2(light.SizeParams.X, light.SizeParams.Y), SceneLight.GetShapeExponent(light.Shape), ShapeColor);

            // The lobes are only drawn faintly over the scene, in the light's color scaled up to full brightness
            var hue = light.Color / MathF.Max(light.Color.MaxComponent(), 0.0001f);
            var color = Color32.FromVector4Clamped(new Vector4(hue, 1f));
            var lobes = With(HelperPasses.Overlay);

            lobes.AddEmissionCurves(range, 0f, lobe: true, color with { A = 64 });
            lobes.AddEmissionCurves(range, 22.5f, lobe: false, color with { A = 32 });
            lobes.AddEmissionCurves(range * (1f - skirt), 0f, lobe: true, color with { A = 16 });
        }

        /// <summary>
        /// Curves in four planes through the forward axis, 45 degrees apart: a lobe of what the light emits,
        /// or the hemisphere it reaches with the circle at its base.
        /// </summary>
        private void AddEmissionCurves(float radius, float startDegrees, bool lobe, Color32 color)
        {
            const int Points = 128;

            for (var plane = 0; plane < 4; plane++)
            {
                var rotation = Matrix4x4.CreateRotationX(float.DegreesToRadians(startDegrees + plane * 45f));
                var previous = Vector3.Zero;

                for (var i = 0; i < Points; i++)
                {
                    var (sin, cos) = MathF.SinCos(i * MathF.PI / (Points - 1));
                    sin = MathF.Max(sin, 0f);

                    var side = cos * radius * (lobe ? MathF.Cbrt(sin) : 1f);
                    var point = Vector3.Transform(new Vector3(sin * radius, 0f, side), rotation);

                    if (i > 0)
                    {
                        AddLine(previous, point, color);
                    }

                    previous = point;
                }

                if (!lobe)
                {
                    AddLine(Vector3.Transform(new Vector3(0f, 0f, radius), rotation), Vector3.Transform(new Vector3(0f, 0f, -radius), rotation), color);
                }
            }

            if (!lobe)
            {
                AddCircle(Vector3.Zero, Vector3.UnitY * radius, Vector3.UnitZ * radius, Points, color);
            }
        }

        private void AddOmni2Light(SceneLight light)
        {
            var range = MathF.Max(light.Range, 1f);
            var size = light.SizeParams;
            var skirt = MathUtils.Saturate(light.FallOff);

            if (light.LuminaireShape is 1 or 2)
            {
                AddTube(size.X, size.Y, capped: light.LuminaireShape == 2);
            }
            else
            {
                AddLatLongSphere(size.X, ShapeColor);
            }

            var outerAngle = Math.Clamp(light.SpotOuterAngle, 1f, 180f);
            var innerAngle = Math.Clamp(light.SpotInnerAngle, 0f, outerAngle);

            var cones = With(HelperPasses.Visible);

            if (innerAngle > 0f && innerAngle < 180f)
            {
                cones.AddConeLines(innerAngle, range, skirt, InnerConeNearColor, InnerConeFarColor);
            }

            if (outerAngle > innerAngle && outerAngle < 180f)
            {
                cones.AddConeLines(outerAngle, range, skirt, OuterConeNearColor, OuterConeFarColor);
            }
        }

        // The tube runs along Y, with optional spokes across its end caps
        private void AddTube(float radius, float halfLength, bool capped)
        {
            const int Segments = 12;

            var halfAxis = Vector3.UnitY * halfLength;
            AddCircle(halfAxis, Vector3.UnitX * radius, Vector3.UnitZ * radius, Segments, ShapeColor);
            AddCircle(-halfAxis, Vector3.UnitX * radius, Vector3.UnitZ * radius, Segments, ShapeColor);

            for (var i = 0; i < Segments; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / Segments);
                var rim = new Vector3(sin * radius, 0f, cos * radius);

                AddLine(rim - halfAxis, rim + halfAxis, ShapeColor);

                if (capped && i < Segments / 2)
                {
                    AddLine(rim - halfAxis, -rim - halfAxis, ShapeColor);
                    AddLine(rim + halfAxis, -rim + halfAxis, ShapeColor);
                }
            }
        }

        // Lines along a cone, split where the skirt starts fading the light out, with the cone's rim at its range
        private void AddConeLines(float angleDegrees, float range, float skirt, Color32 nearColor, Color32 farColor)
        {
            const int Lines = 16;

            var (sinAngle, cosAngle) = MathF.SinCos(float.DegreesToRadians(angleDegrees));

            for (var i = 0; i < Lines; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / Lines);
                var end = new Vector3(cosAngle, sinAngle * sin, sinAngle * cos) * range;

                AddLine(Vector3.Zero, end * skirt, nearColor);
                AddLine(end * skirt, end, farColor);
            }

            AddCircle(Vector3.UnitX * (cosAngle * range), Vector3.UnitY * (sinAngle * range), Vector3.UnitZ * (sinAngle * range), 48, farColor);
        }

        /// <summary>
        /// The cone of a spot light out to its range, and a dome over it whose radius follows the light's intensity
        /// from the inner to the outer angle.
        /// </summary>
        private void AddLightCone(SceneLight light, float falloff)
        {
            const float StepDegrees = 24f;
            const float RingDegrees = 6f;

            var range = light.Range;
            var innerAngle = light.SpotInnerAngle;
            var outerAngle = light.SpotOuterAngle;
            var color = light.Color;

            if (outerAngle > 90f || range <= 0f)
            {
                return;
            }

            if (outerAngle < 90f)
            {
                var baseRadius = MathF.Tan(float.DegreesToRadians(outerAngle)) * range;
                var coneColor = Color32.FromVector4Clamped(new Vector4(color, 180f / 255f));
                var previous = new Vector3(range, 0f, baseRadius);

                for (var angle = StepDegrees; angle <= 361f; angle += StepDegrees)
                {
                    var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
                    var point = new Vector3(range, sin * baseRadius, cos * baseRadius);

                    AddLine(previous, point, coneColor);
                    AddLine(Vector3.Zero, point, coneColor);
                    previous = point;
                }
            }

            var cosInner = MathF.Cos(float.DegreesToRadians(innerAngle));
            var cosOuter = MathF.Cos(float.DegreesToRadians(outerAngle));
            var previousDistance = range;
            var previousRadius = 0f;

            for (var ring = RingDegrees; ring < outerAngle + RingDegrees; ring += RingDegrees)
            {
                var theta = MathF.Min(ring, outerAngle);
                var (sinTheta, cosTheta) = MathF.SinCos(float.DegreesToRadians(theta));
                var intensity = 1f;

                if (theta > innerAngle)
                {
                    intensity = (cosTheta - cosOuter) / (cosInner - cosOuter);

                    if (falloff is not 0f and not 1f)
                    {
                        intensity = MathF.Pow(intensity, falloff);
                    }
                }

                var distance = cosTheta * intensity * range;
                var radius = sinTheta * intensity * range;
                var ringColor = Color32.FromVector4Clamped(new Vector4(color * intensity, 180f / 255f));

                for (var angle = 0f; angle < 360f; angle += StepDegrees)
                {
                    var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
                    var (nextSin, nextCos) = MathF.SinCos(float.DegreesToRadians(angle + StepDegrees));
                    var point = new Vector3(distance, sin * radius, cos * radius);

                    AddLine(point, new Vector3(distance, nextSin * radius, nextCos * radius), ringColor);
                    AddLine(new Vector3(previousDistance, sin * previousRadius, cos * previousRadius), point, ringColor);
                }

                previousDistance = distance;
                previousRadius = radius;
            }
        }

        /// <summary>
        /// A superellipse in the plane facing +X, spanning <paramref name="halfSize"/> along Y and Z. An exponent of
        /// one is an ellipse, and lower ones square it off down to a rectangle at zero.
        /// </summary>
        private static Vector3 SuperellipsePoint(Vector3 center, Vector2 halfSize, float exponent, float angle)
        {
            var (sin, cos) = MathF.SinCos(angle);

            return center + new Vector3(
                0f,
                MathF.CopySign(MathF.Pow(MathF.Abs(sin), exponent), sin) * halfSize.X,
                MathF.CopySign(MathF.Pow(MathF.Abs(cos), exponent), cos) * halfSize.Y);
        }

        private void AddSuperellipse(Vector3 center, Vector2 halfSize, float exponent, Color32 color)
        {
            const int Segments = 48;

            var previous = SuperellipsePoint(center, halfSize, exponent, 0f);

            for (var i = 1; i <= Segments; i++)
            {
                var point = SuperellipsePoint(center, halfSize, exponent, i * MathF.Tau / Segments);

                // A rectangle puts many points on each corner
                if (point != previous)
                {
                    AddLine(previous, point, color);
                }

                previous = point;
            }
        }

        private void AddSuperellipseConnectors(Vector3 fromCenter, Vector2 fromHalfSize, Vector3 toCenter, Vector2 toHalfSize, float exponent, float startDegrees, float stepDegrees, Color32 color)
        {
            for (var angle = startDegrees; angle < 360f; angle += stepDegrees)
            {
                var radians = float.DegreesToRadians(angle);

                AddLine(SuperellipsePoint(fromCenter, fromHalfSize, exponent, radians), SuperellipsePoint(toCenter, toHalfSize, exponent, radians), color);
            }
        }

        // Twelve meridians through the poles on Z, and the rings of latitude between them
        private void AddLatLongSphere(float radius, Color32 color)
        {
            const int Segments = 12;
            const int Rings = 12;

            if (radius <= 0f)
            {
                return;
            }

            for (var ring = 0; ring < Rings - 1; ring++)
            {
                var (sinTheta, cosTheta) = MathF.SinCos(ring * MathF.PI / (Rings - 1));
                var (nextSinTheta, nextCosTheta) = MathF.SinCos((ring + 1) * MathF.PI / (Rings - 1));

                for (var segment = 0; segment < Segments; segment++)
                {
                    var (sin, cos) = MathF.SinCos(segment * MathF.Tau / Segments);
                    var (nextSin, nextCos) = MathF.SinCos((segment + 1) * MathF.Tau / Segments);
                    var point = new Vector3(cos * sinTheta, sin * sinTheta, cosTheta) * radius;

                    AddLine(point, new Vector3(cos * nextSinTheta, sin * nextSinTheta, nextCosTheta) * radius, color);

                    if (ring > 0)
                    {
                        AddLine(point, new Vector3(nextCos * sinTheta, nextSin * sinTheta, cosTheta) * radius, color);
                    }
                }
            }
        }

        // Three circles around the axes
        private void AddGreatCircles(float radius, Color32 color)
        {
            const int Segments = 48;

            if (radius <= 0f)
            {
                return;
            }

            AddCircle(Vector3.Zero, Vector3.UnitX * radius, Vector3.UnitY * radius, Segments, color);
            AddCircle(Vector3.Zero, Vector3.UnitX * radius, Vector3.UnitZ * radius, Segments, color);
            AddCircle(Vector3.Zero, Vector3.UnitY * radius, Vector3.UnitZ * radius, Segments, color);
        }

        // A circle around center, spanned by two perpendicular radii
        private void AddCircle(Vector3 center, Vector3 axisA, Vector3 axisB, int segments, Color32 color)
        {
            var previous = center + axisB;

            for (var i = 1; i <= segments; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / segments);
                var point = center + axisA * sin + axisB * cos;

                AddLine(previous, point, color);
                previous = point;
            }
        }

        private EntityHelperLines With(HelperPasses newPasses) => new(output, transform, newPasses);

        // The faces of a box, tinted where seen and fainter over the scene, wound to face outwards
        private void AddBoxFaces(in AABB box, Color32 color)
        {
            Span<Vector3> corners = stackalloc Vector3[8];
            GetCorners(box, corners);

            foreach (ref var corner in corners)
            {
                corner = Vector3.Transform(corner, transform);
            }

            // Corner indices of each face, counter-clockwise seen from outside
            ReadOnlySpan<int> faces =
            [
                0, 2, 3, 1, // -Z
                4, 5, 7, 6, // +Z
                0, 1, 5, 4, // -Y
                2, 6, 7, 3, // +Y
                0, 4, 6, 2, // -X
                1, 3, 7, 5, // +X
            ];

            var faceColor = color with { A = BoxFaceAlpha };
            var overlayColor = color with { A = BoxOverlayFaceAlpha };

            for (var i = 0; i < faces.Length; i += 4)
            {
                ReadOnlySpan<int> quad = [faces[i], faces[i + 1], faces[i + 2], faces[i], faces[i + 2], faces[i + 3]];

                foreach (var corner in quad)
                {
                    output.Faces.Add(new SimpleVertex(corners[corner], faceColor));
                    output.OverlayFaces.Add(new SimpleVertex(corners[corner], overlayColor));
                }
            }
        }

        private void AddLine(Vector3 from, Vector3 to, Color32 color)
            => output.AddLine(Vector3.Transform(from, transform), Vector3.Transform(to, transform), color, passes);
    }
}
