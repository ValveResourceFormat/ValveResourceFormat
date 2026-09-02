using System.Linq;
using System.IO;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Mesh = SharpGLTF.Schema2.Mesh;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VWorld = ValveResourceFormat.ResourceTypes.World;
using VWorldNode = ValveResourceFormat.ResourceTypes.WorldNode;

namespace ValveResourceFormat.IO;

/// <summary>
/// Exports what a world is made of: the models its world nodes place, the entities its lumps declare, and
/// the lights they carry.
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
            var entityLumpResource = FileLoader.LoadFileCompiled(lumpName);
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
            var skinName = entity.GetStringProperty("skin");
            if (skinName == "0" || skinName == "default")
            {
                skinName = null;
            }

            // todo: rendercolor might sometimes be vec4, which holds renderamt
            var rendercolor = entity.GetColor32Property("rendercolor");
            var renderamt = entity.GetFloatProperty("renderamt", 1.0f);

            if (renderamt > 1f)
            {
                renderamt /= 255f;
            }

            rendercolor = ColorSpace.SrgbGammaToLinear(rendercolor);
            var tintColor = new Vector4(rendercolor, renderamt);

            // Add meshes and their skeletons
            LoadModel(exportedModel, scene, model, Path.GetFileNameWithoutExtension(modelName),
                transform, tintColor, skinName, entity);

            var phys = model.GetEmbeddedPhys();
            if (phys == null)
            {
                var refPhysicsPaths = model.GetReferencedPhysNames().ToArray();
                if (refPhysicsPaths.Length != 0)
                {
                    var newResource = FileLoader.LoadFileCompiled(refPhysicsPaths.First());
                    if (newResource?.DataBlock is PhysAggregateData physFile)
                    {
                        phys = physFile;
                    }
                }
            }

            if (phys != null)
            {
                PhysicsToExport.Add((phys, className, transform));
            }
        }
    }

    private static string? GetSkinPathFromModel(VModel model, string skinName)
    {
        var materialGroupForSkin = model.GetMaterialGroups()
            .SingleOrDefault(group => group.Name == skinName);

        // Given these are at the model level, and otherwise pull materials from drawcalls
        // on the mesh, not sure how they correlate if there's more than one here
        // So just take the first one and hope for the best
        return materialGroupForSkin.Materials?[0];
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
        LoadWorldNodeModels(exportedModel, scene, worldNode);

        WriteModelFile(exportedModel, fileName);
    }

    private void LoadWorldNodeModels(ModelRoot exportedModel, Scene scene, VWorldNode worldNode)
    {
        foreach (var sceneObject in worldNode.SceneObjects)
        {
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

            LoadModel(exportedModel, scene, model, name, matrix, tintColor);
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

        var envLight = exportedModel
            .CreatePunctualLight(PunctualLightType.Directional)
            .WithColor(color, intensity * PbrWattsTolumens);

        return envLight;
    }
}
