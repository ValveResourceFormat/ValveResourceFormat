using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// The eyeballs of a Source 1 model: their aim at the viewer, the eyelids that follow it and the material parameters of the eye shaders.
    /// </summary>
    internal partial class Source1ModelSceneNode
    {
        private const string EyeOriginUniform = "g_vEyeOrigin";
        private const string EyeUpUniform = "g_vEyeUp";
        private const string IrisProjectionUUniform = "g_vIrisProjectionU";
        private const string IrisProjectionVUniform = "g_vIrisProjectionV";

        // The eyes attachment is where the eyes look from, and the distance to its plane that the target is kept beyond
        private const string EyesAttachmentName = "eyes";
        private const float MinimumEyeTargetDistance = 6f;

        private readonly struct EyeFrame(Vector3 up, Vector3 right, Vector3 forward)
        {
            public Vector3 Up { get; } = up;
            public Vector3 Right { get; } = right;
            public Vector3 Forward { get; } = forward;
        }

        // The shown meshes that hold an eyeball
        private readonly List<MeshEntry> eyeEntries = [];
        private readonly int eyesAttachment;
        private bool hasFacsEyelids;
        private Vector3 cameraPosition;
        private Matrix4x4[]? bindBoneToModel;

        private static bool HasFacsEyeballs(Source1Model model)
        {
            foreach (var part in model.BodyParts)
            {
                foreach (var subModel in part.Models)
                {
                    foreach (var eyeball in subModel.Eyeballs)
                    {
                        if (!eyeball.NonFacs)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private Source1Eyeball? FindEyeball(int partIndex, int subIndex, Source1Mesh source)
        {
            if (source.MaterialType != 1)
            {
                return null;
            }

            var eyeballs = Model.BodyParts[partIndex].Models[subIndex].Eyeballs;
            return (uint)source.MaterialParam < (uint)eyeballs.Length ? eyeballs[source.MaterialParam] : null;
        }

        private static bool UsesEyeUniforms(RenderMaterial material)
            => material.Shader.UniformNames.Contains(IrisProjectionUUniform);

        // An eyeball mesh draws with a copy of its material, as the eye shaders take the eyeball of the mesh as uniforms
        private RenderMaterial GetEntryMaterial(Source1Mesh source, Source1Eyeball? eyeball, Dictionary<int, RenderMaterial> eyeMaterials)
        {
            var materialIndex = GetMaterialIndex(source, Skin);
            var material = GetMaterial(materialIndex);

            if (eyeball == null || !UsesEyeUniforms(material))
            {
                return material;
            }

            if (eyeMaterials.TryGetValue(materialIndex, out var instance))
            {
                return instance;
            }

            instance = Scene.RendererContext.MaterialLoader.CreateSource1MaterialInstance(material, GetShaderArguments());

            foreach (var (name, texture) in material.Textures)
            {
                instance.Textures[name] = texture;
            }

            foreach (var (name, value) in material.IntParams)
            {
                instance.IntParams[name] = value;
            }

            foreach (var (name, value) in material.FloatParams)
            {
                instance.FloatParams[name] = value;
            }

            foreach (var (name, value) in material.VectorParams)
            {
                instance.VectorParams[name] = value;
            }

            eyeMaterials[materialIndex] = instance;
            return instance;
        }

        private void TrackEyeTarget(Vector3 position)
        {
            if (position == cameraPosition)
            {
                return;
            }

            cameraPosition = position;
            flexDirty |= hasFacsEyelids;
        }

        private Matrix4x4 GetBoneToModel(int bone)
        {
            if ((uint)bone >= (uint)Model.Bones.Length)
            {
                return Matrix4x4.Identity;
            }

            if (animation != null)
            {
                return boneToModel[bone];
            }

            if (bindBoneToModel == null)
            {
                bindBoneToModel = new Matrix4x4[Model.Bones.Length];

                for (var i = 0; i < bindBoneToModel.Length; i++)
                {
                    Matrix4x4.Invert(Model.Bones[i].PoseToBone, out bindBoneToModel[i]);
                }
            }

            return bindBoneToModel[bone];
        }

        private Matrix4x4 GetBoneToWorld(int bone) => GetBoneToModel(bone) * Transform;

        // The point the eyes aim at in world space: the camera when it is in front of the eyes, within the turn the
        // eyes are allowed, and straight ahead otherwise
        private Vector3 GetEyeTarget()
        {
            if (eyesAttachment < 0)
            {
                return cameraPosition;
            }

            var attachment = Model.Attachments[eyesAttachment];
            var attachmentToWorld = attachment.Local * GetBoneToWorld(attachment.BoneIndex);

            if (!Matrix4x4.Invert(attachmentToWorld, out var worldToAttachment))
            {
                return cameraPosition;
            }

            var local = Vector3.Transform(cameraPosition, worldToAttachment);
            var distance = local.Length();

            if (local.X <= 0f || distance <= 0f)
            {
                local = new Vector3(MathF.Max(distance, MinimumEyeTargetDistance), 0f, 0f);
                return Vector3.Transform(local, attachmentToWorld);
            }

            local.X = MathF.Max(local.X, MinimumEyeTargetDistance);
            distance = local.Length();
            local /= distance;

            var maxDeflection = Model.MaxEyeDeflection;

            if (local.X < maxDeflection)
            {
                var sideLengthSquared = (local.Y * local.Y) + (local.Z * local.Z);

                if (sideLengthSquared > 0f)
                {
                    var scale = MathF.Sqrt((1f - (maxDeflection * maxDeflection)) / sideLengthSquared);
                    local = new Vector3(maxDeflection, local.Y * scale, local.Z * scale);
                }
                else
                {
                    local = Vector3.UnitX;
                }
            }

            return Vector3.Transform(local * distance, attachmentToWorld);
        }

        private static int FindEyesAttachment(Source1Model model)
        {
            var attachments = model.Attachments;

            for (var i = 0; i < attachments.Length; i++)
            {
                if (attachments[i].Name.Equals(EyesAttachmentName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        // The frame of an eyeball turned toward a target, in the space of its bone
        private static EyeFrame GetEyeFrame(Source1Eyeball eyeball, Vector3 target)
        {
            var up = eyeball.Up;
            var toTarget = target - eyeball.Origin;
            var forward = toTarget.LengthSquared() > 0f ? Vector3.Normalize(toTarget) : eyeball.Forward;

            var right = MathUtils.SafeNormalize(Vector3.Cross(forward, up));

            // The eye looks a little to the side of its target
            forward = MathUtils.SafeNormalize(forward + (right * (eyeball.ZOffset * 2f)));
            right = MathUtils.SafeNormalize(Vector3.Cross(forward, up));
            up = MathUtils.SafeNormalize(Vector3.Cross(right, forward));

            return new EyeFrame(up, right, forward);
        }

        private bool HasEyeMaterials()
        {
            foreach (var entry in eyeEntries)
            {
                if (entry.EyeMaterials.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateEyes(Vector3 target)
        {
            foreach (var entry in eyeEntries)
            {
                foreach (var material in entry.EyeMaterials.Values)
                {
                    SetEyeUniforms(material, entry.Eyeball!, target);
                }
            }
        }

        // Positions the iris texture on the eyeball: its coordinates are the distance along the right and up of the eye
        // from the center of the eyeball, scaled by the size of the iris, with the center of the texture on the eye
        private void SetEyeUniforms(RenderMaterial material, Source1Eyeball eyeball, Vector3 worldTarget)
        {
            var boneToWorld = GetBoneToWorld(eyeball.Bone);

            if (!Matrix4x4.Invert(boneToWorld, out var worldToBone))
            {
                return;
            }

            var frame = GetEyeFrame(eyeball, Vector3.Transform(worldTarget, worldToBone));

            var origin = Vector3.Transform(eyeball.Origin, boneToWorld);
            var right = MathUtils.SafeNormalize(Vector3.TransformNormal(frame.Right, boneToWorld));
            var up = MathUtils.SafeNormalize(Vector3.TransformNormal(frame.Up, boneToWorld));

            var scale = 1f / eyeball.IrisScale;

            if (scale > 0f)
            {
                scale = 1f / scale;
            }

            var projectionU = right * -scale;
            var projectionV = up * -scale;

            material.VectorParams[EyeOriginUniform] = new Vector4(origin, 0f);
            material.VectorParams[EyeUpUniform] = new Vector4(eyeball.Up, 0f);
            material.VectorParams[IrisProjectionUUniform] = new Vector4(projectionU, 0.5f - Vector3.Dot(origin, projectionU));
            material.VectorParams[IrisProjectionVUniform] = new Vector4(projectionV, 0.5f - Vector3.Dot(origin, projectionV));
        }

        // Sets the flexes that move the eyelids with the eye, as the weight of the lid controllers allows
        private void ApplyEyelids(Vector3 target)
        {
            if (!hasFacsEyelids)
            {
                return;
            }

            for (var part = 0; part < Model.BodyParts.Length; part++)
            {
                var models = Model.BodyParts[part].Models;
                var subModel = bodyGroupSelection[part];

                if ((uint)subModel >= (uint)models.Length)
                {
                    continue;
                }

                foreach (var eyeball in models[subModel].Eyeballs)
                {
                    if (!eyeball.NonFacs)
                    {
                        ApplyEyelids(eyeball, target);
                    }
                }
            }
        }

        private void ApplyEyelids(Source1Eyeball eyeball, Vector3 worldTarget)
        {
            if (!Matrix4x4.Invert(GetBoneToWorld(eyeball.Bone), out var worldToBone))
            {
                return;
            }

            var frame = GetEyeFrame(eyeball, Vector3.Transform(worldTarget, worldToBone));

            var upperAngle = GetLidAngle(eyeball.UpperFlexes, eyeball.UpperTargets, eyeball.Radius);
            var lowerAngle = GetLidAngle(eyeball.LowerFlexes, eyeball.LowerTargets, eyeball.Radius);

            SetLidWeight(eyeball.UpperLidFlex, upperAngle, eyeball, frame);
            SetLidWeight(eyeball.LowerLidFlex, lowerAngle, eyeball, frame);
        }

        // The angle of a lid, from the weights of its raised, neutral and lowered controls
        private float GetLidAngle(int[] flexes, float[] targets, float radius)
        {
            var angle = 0f;

            for (var i = 0; i < flexes.Length; i++)
            {
                if ((uint)flexes[i] >= (uint)flexWeights.Length)
                {
                    continue;
                }

                var normalizedTarget = radius > 0f ? targets[i] / radius : 0f;
                angle += flexWeights[flexes[i]] * MathF.Asin(Math.Clamp(normalizedTarget, -1f, 1f));
            }

            return angle;
        }

        // The lid flex holds how far the lid sits along the up direction of the eyeball
        private void SetLidWeight(int flex, float angle, Source1Eyeball eyeball, EyeFrame frame)
        {
            if ((uint)flex >= (uint)flexWeights.Length)
            {
                return;
            }

            var (sin, cos) = MathF.SinCos(angle);
            var position = (frame.Up * (sin * eyeball.Radius)) + (frame.Forward * (cos * eyeball.Radius));

            flexWeights[flex] = Vector3.Dot(position, eyeball.Up);
        }
    }
}
