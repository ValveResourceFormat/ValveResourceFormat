using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>post_processing_volume</c>. Registers its post processing with the scene: a master volume applies
/// everywhere, any other one while the camera is inside its model.
/// </summary>
public sealed class PostProcessingVolume : BaseModelEntity
{
    /// <summary>Gets the post processing this entity registered.</summary>
    public ScenePostProcessVolume? Volume { get; private set; }

    private Model? volumeModel;

    /// <summary>Initializes a <c>post_processing_volume</c> from its keyvalues.</summary>
    public PostProcessingVolume(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override SceneNode? CreateRootNode()
    {
        // A volume without a model has no editor marker
        if (string.IsNullOrEmpty(ModelName))
        {
            return null;
        }

        if (EntitySystem.FileLoader.LoadFileCompiled(ModelName)?.DataBlock is not Model model)
        {
            EntitySystem.Logger.LogWarning("Post Process model failed to load file \"{Model}\"", ModelName);
            return null;
        }

        volumeModel = model;

        return base.CreateRootNode();
    }

    // The model only bounds the volume, it must never block the player
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        var transform = Transform;

        var postProcess = new ScenePostProcessVolume(Scene)
        {
            ExposureSettings = ExposureSettings.LoadFromEntity(KeyValues),
            FadeTime = KeyValues.GetFloatProperty("fadetime", 1.0f),
            UseExposure = KeyValues.GetBooleanProperty("enableexposure"),
            IsMaster = KeyValues.GetBooleanProperty("master"),
            IsEnabled = !KeyValues.GetBooleanProperty("startdisabled"),
            Transform = transform, // needed if model is used
        };

        var postProcessResourceFilename = KeyValues.GetStringProperty("postprocessing");

        if (postProcessResourceFilename != null
            && EntitySystem.FileLoader.LoadFileCompiled(postProcessResourceFilename)?.DataBlock is PostProcessing postProcessAsset)
        {
            postProcess.LoadPostProcessResource(postProcessAsset);
        }

        if (volumeModel != null)
        {
            postProcess.ModelVolume = volumeModel;

            // Local volumes apply while the camera is inside their trigger shape
            var volumePhysics = EntityCollider.LoadPhysics(volumeModel, EntitySystem.FileLoader);

            if (volumePhysics != null)
            {
                postProcess.Collider = new EntityCollider(volumePhysics)
                {
                    Transform = transform,
                };
            }
        }

        Scene.PostProcessInfo.AddPostProcessVolume(postProcess);

        Volume = postProcess;
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => Volume?.IsEnabled = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => Volume?.IsEnabled = false;

    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data)
    {
        if (Volume != null)
        {
            Volume.IsEnabled = !Volume.IsEnabled;
        }
    }
}
