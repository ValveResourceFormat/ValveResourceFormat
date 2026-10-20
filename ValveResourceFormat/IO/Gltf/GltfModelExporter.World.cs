using System.Globalization;
using System.IO;
using System.Linq;
using SharpGLTF.Schema2;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Mesh = SharpGLTF.Schema2.Mesh;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VWorld = ValveResourceFormat.ResourceTypes.World;
using VWorldNode = ValveResourceFormat.ResourceTypes.WorldNode;

namespace ValveResourceFormat.IO;

/// <summary>
/// Exports the models world nodes place, the entities lumps declare, and the lights they carry.
/// </summary>
public partial class GltfModelExporter
{
    /// <summary>
    /// Export a Valve VWRLD to GLTF.
    /// </summary>
    /// <param name="resourceName">The name of the resource being exported.</param>
    /// <param name="fileName">Target file name.</param>
    /// <param name="world">The world resource to export.</param>
    private void ExportToFile(string resourceName, string? fileName, VWorld world)
    {
        var exportedModel = CreateModelRoot(resourceName, out var scene);

        LightmapUvScale = world.GetLightmapUvScale();
        SkipToolsMaterials = true;

        // First the WorldNodes
        foreach (var worldNodeName in world.GetWorldNodeNames())
        {
            if (worldNodeName == null)
            {
                continue;
            }

            var worldResource = FileLoader.LoadFile(worldNodeName + ".vwnod_c");
            if (worldResource == null)
            {
                continue;
            }

            var worldNode = (VWorldNode)worldResource.DataBlock!;
            LoadWorldNodeModels(exportedModel, scene, worldNode);
        }

        // Then the Entities
        foreach (var lumpName in world.GetEntityLumpNames())
        {
            if (lumpName == null)
            {
                continue;
            }
            using var entityLumpResource = FileLoader.LoadFileCompiled(lumpName);
            if (entityLumpResource == null)
            {
                continue;
            }

            var entityLump = (VEntityLump)entityLumpResource.DataBlock!;

            LoadEntityMeshes(exportedModel, scene, entityLump, Matrix4x4.Identity);
        }

        WriteModelFile(exportedModel, fileName);

        ExportPhysicsIfAny(resourceName, fileName);
    }

    /// <summary>
    /// Export a list of entities to GLTF.
    /// </summary>
    /// <param name="resourceName">The name of the resource being exported.</param>
    /// <param name="fileName">Target file name.</param>
    /// <param name="entityLump">The entity lump resource to export.</param>
    private void ExportToFile(string resourceName, string? fileName, VEntityLump entityLump)
    {
        var exportedModel = CreateModelRoot(resourceName, out var scene);
        SkipToolsMaterials = true;

        LoadEntityMeshes(exportedModel, scene, entityLump, Matrix4x4.Identity);

        WriteModelFile(exportedModel, fileName);

        ExportPhysicsIfAny(resourceName, fileName);
    }

    private void LoadEntityMeshes(ModelRoot exportedModel, Scene scene, VEntityLump entityLump, Matrix4x4 rootTransform)
    {
        var traversed = EntityLumpTraversal.EnumerateEntities(
            entityLump,
            FileLoader,
            rootTransform,
            onMissingChildLump: name => ProgressReporter?.Report($"Failed to find child entity lump with name {name}."));

        foreach (var (entity, parentTransform, _) in traversed)
        {
            var transform = EntityTransformHelper.ToTransformationMatrix(entity) * parentTransform;
            var modelName = entity.GetStringProperty("model");
            var className = entity.GetStringProperty("classname");

            if (string.IsNullOrEmpty(modelName))
            {
                // Add environment lights with KHR_lights_punctual
                // https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_lights_punctual/README.md
                // TODO: Add point and spot lights
                if (className == "light_environment")
                {
                    if (!Matrix4x4.Decompose(transform, out _, out var rotation, out var positionVector))
                    {
                        throw new InvalidOperationException("Matrix decompose failed");
                    }

                    // glTF directional lights emit along node-local -Z; orient the node so that
                    // -Z matches the sun's forward (travel) direction, i.e. the entity's local +X.
                    // Taken from the decomposed transform rather than the entity's own angles, so a
                    // light inside a rotated child lump points where the lump put it.
                    var direction = Vector3.Transform(Vector3.UnitX, rotation);

                    var directionGltf = Vector3.Transform(direction, SourceToGltfRotation);
                    var positionGltf = Vector3.Transform(positionVector, TransformSourceToGltf);

                    // 'up' only anchors the meaningless roll around the beam, but CreateWorld degenerates
                    // when it is parallel to the direction, so for a near-vertical sun (along Y) use Z instead.
                    var up = MathF.Abs(directionGltf.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

                    var node = scene.CreateNode(className);
                    node.PunctualLight = CreateGltfLightEnvironment(exportedModel, entity);
                    node.LocalMatrix = Matrix4x4.CreateWorld(positionGltf, directionGltf, up);
                }

                continue;
            }

            if (className == "csgo_player_previewmodel")
            {
                continue;
            }

            var modelResource = FileLoader.LoadFileCompiled(modelName);
            if (modelResource == null)
            {
                continue;
            }

            // TODO: skybox/skydome

            var model = (VModel)modelResource.DataBlock!;
            var skinName = GetSkinName(entity, "skin");

            // Add meshes and their skeletons
            LoadModel(exportedModel, scene, model, Path.GetFileNameWithoutExtension(modelName),
                transform, entity.GetRenderTint(), skinName, entity);

            var phys = GetModelPhysics(model);
            if (phys != null)
            {
                PhysicsToExport.Add((phys, className, transform));
            }
        }
    }

    private static string? GetSkinName(KVObject placement, string key)
    {
        var skinName = placement.GetStringProperty(key);
        return skinName is "" or "0" or "default" ? null : skinName;
    }

    /// <summary>
    /// Maps each material of the model's default material group to the one at the same index in the named group.
    /// </summary>
    /// <returns>The replacement table, or <see langword="null"/> when the named group is missing or is the default one.</returns>
    private static Dictionary<string, string>? GetSkinMaterialTable(VModel model, string skinName)
    {
        var materialGroups = model.GetMaterialGroups().ToArray();
        var skinIndex = Array.FindIndex(materialGroups, group => group.Name == skinName);

        if (skinIndex <= 0)
        {
            return null;
        }

        var materialTable = new Dictionary<string, string>();

        foreach (var (material, replacement) in materialGroups[0].Materials.Zip(materialGroups[skinIndex].Materials))
        {
            materialTable[material] = replacement;
        }

        return materialTable;
    }

    private static Dictionary<KVObject, string>? GetDrawCallMaterials(VMesh mesh, int meshIndex, Dictionary<string, string>? skinMaterialTable,
        VWorldNode.MaterialOverride[] materialOverrides)
    {
        if (skinMaterialTable == null && materialOverrides.Length == 0)
        {
            return null;
        }

        var drawCallMaterials = new Dictionary<KVObject, string>(ReferenceEqualityComparer.Instance);
        var drawCallIndex = 0;

        foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var material = VWorldNode.MaterialOverride.Find(materialOverrides, meshIndex, drawCallIndex++);

                if (material == null && skinMaterialTable != null && VMesh.GetMaterialName(drawCall) is { } ownMaterial)
                {
                    material = skinMaterialTable.GetValueOrDefault(ownMaterial);
                }

                if (material != null)
                {
                    drawCallMaterials[drawCall] = material;
                }
            }
        }

        return drawCallMaterials;
    }

    private static string GetOverriddenMeshName(string meshName, int meshIndex, VWorldNode.MaterialOverride[] materialOverrides)
    {
        var meshOverrides = materialOverrides
            .Where(materialOverride => !string.IsNullOrEmpty(materialOverride.Material) && (materialOverride.IsWholeModel || materialOverride.SubSceneObject == meshIndex))
            .Select(static materialOverride =>
            {
                var material = Path.GetFileNameWithoutExtension(materialOverride.Material);
                return materialOverride.IsWholeModel ? material : string.Create(CultureInfo.InvariantCulture, $"{material}@{materialOverride.DrawCallIndex}");
            });

        return string.Join('.', [meshName, .. meshOverrides]);
    }

    /// <summary>
    /// Export a Valve VWNOD to GLTF.
    /// </summary>
    /// <param name="resourceName">The name of the resource being exported.</param>
    /// <param name="fileName">Target file name.</param>
    /// <param name="worldNode">The worldNode resource to export.</param>
    private void ExportToFile(string resourceName, string? fileName, VWorldNode worldNode)
    {
        var exportedModel = CreateModelRoot(resourceName, out var scene);
        SkipToolsMaterials = true;

        LoadWorldNodeModels(exportedModel, scene, worldNode);

        WriteModelFile(exportedModel, fileName);
    }

    private void LoadWorldNodeModels(ModelRoot exportedModel, Scene scene, VWorldNode worldNode)
    {
        var materialOverrides = worldNode.MaterialOverrides.ToLookup(static materialOverride => materialOverride.SceneObjectIndex);

        for (var sceneObjectIndex = 0; sceneObjectIndex < worldNode.SceneObjects.Count; sceneObjectIndex++)
        {
            var sceneObject = worldNode.SceneObjects[sceneObjectIndex];
            var renderableModel = sceneObject.GetStringProperty("m_renderableModel");
            if (renderableModel == null)
            {
                continue;
            }

            var modelResource = FileLoader.LoadFileCompiled(renderableModel);
            if (modelResource == null)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(renderableModel);
            var model = (VModel)modelResource.DataBlock!;
            var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();
            var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();

            if (tintColor == Vector4.Zero)
            {
                tintColor = Vector4.One;
            }

            LoadModel(exportedModel, scene, model, name, matrix, tintColor, GetSkinName(sceneObject, "m_skin"),
                materialOverrides: [.. materialOverrides[(uint)sceneObjectIndex]]);
        }

        foreach (var sceneObject in worldNode.AggregateSceneObjects)
        {
            var renderableModel = sceneObject.GetStringProperty("m_renderableModel");

            if (renderableModel != null)
            {
                var modelResource = FileLoader.LoadFileCompiled(renderableModel);

                if (modelResource == null)
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(renderableModel);
                var model = (VModel)modelResource.DataBlock!;

                if (!AggregateCreateFragments(exportedModel, scene, model, sceneObject, name))
                {
                    LoadModel(exportedModel, scene, model, name, Matrix4x4.Identity, Vector4.One);
                }
            }
        }
    }

    private static PunctualLight CreateGltfLightEnvironment(ModelRoot exportedModel, VEntityLump.Entity entity)
    {
        var intensity = entity.GetFloatProperty("brightness", 1f);
        var color = entity.GetColor32Property("color");
        color = ColorSpace.SrgbGammaToLinear(color);

        var envLight = exportedModel.CreatePunctualLight(PunctualLightType.Directional);
        envLight.Color = color;
        envLight.Intensity = intensity * PbrWattsTolumens;
        envLight.Range = float.PositiveInfinity;

        return envLight;
    }
}
