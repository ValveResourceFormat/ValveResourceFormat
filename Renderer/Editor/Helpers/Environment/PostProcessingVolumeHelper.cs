using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The post processing of a <c>post_processing_volume</c>: its exposure and the color grading it names.
/// </summary>
public sealed class PostProcessingVolumeHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Initializes the post processing of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public PostProcessingVolumeHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var scene = Owner.Scene;
        var keyValues = Owner.KeyValues;

        var volume = new ScenePostProcessVolume(scene)
        {
            ExposureSettings = ExposureSettings.LoadFromEntity(keyValues),
            FadeTime = keyValues.GetFloatProperty("fadetime", 1.0f),
            UseExposure = keyValues.GetBooleanProperty("enableexposure"),
            IsMaster = keyValues.GetBooleanProperty("master"),
            IsEnabled = !keyValues.GetBooleanProperty("startdisabled"),
            Transform = Owner.Transform,
        };

        if (keyValues.GetStringProperty("postprocessing") is { } resourceName
            && scene.RendererContext.FileLoader.LoadFileCompiled(resourceName)?.DataBlock is PostProcessing postProcessing)
        {
            volume.LoadPostProcessResource(postProcessing);
        }

        scene.PostProcessInfo.AddPostProcessVolume(volume);
    }
}
