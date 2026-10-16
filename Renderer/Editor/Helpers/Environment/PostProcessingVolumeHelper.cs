using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The post processing of a <c>post_processing_volume</c>: its exposure and the post processing resource it
/// names, under the keys its declaration gives.
/// </summary>
public sealed class PostProcessingVolumeHelper : EntityHelper
{
    /// <summary>Initializes the post processing of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public PostProcessingVolumeHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <summary>Gets the key of the post processing resource the declaration gives.</summary>
    public string ResourceKey => GetArgument(0) is { Length: > 0 } key ? key : "postprocessing";

    /// <summary>Gets the key of the fade time the declaration gives.</summary>
    public string FadeTimeKey => GetArgument(1) is { Length: > 0 } key ? key : "fadetime";

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var scene = Owner.Scene;
        var defaults = new ExposureSettings();

        // An exposure limit of zero or less is given as a power of two instead. Every volume is used,
        // whether or not it starts disabled.
        var volume = new ScenePostProcessVolume(scene)
        {
            ExposureSettings = defaults with
            {
                ExposureMin = GetExposureLimit("minexposure", "minlogexposure"),
                ExposureMax = GetExposureLimit("maxexposure", "maxlogexposure"),
                ExposureSpeedUp = GetFloatKey("exposurespeedup", defaults.ExposureSpeedUp),
                ExposureSpeedDown = GetFloatKey("exposurespeeddown", defaults.ExposureSpeedDown),
                ExposureSmoothingRange = GetFloatKey("exposuresmoothingrange", defaults.ExposureSmoothingRange),
                ExposureCompensation = GetFloatKey("exposurecompensation", defaults.ExposureCompensation),
                AutoExposureEnabled = GetBooleanKey("enableexposure", false),
            },
            FadeTime = GetFloatKey(FadeTimeKey, 1f),
            UseExposure = GetBooleanKey("enableexposure", false),
            IsMaster = GetBooleanKey("master", false),
            IsEnabled = true,
            Transform = Owner.Transform,
        };

        if (GetStringKey(ResourceKey) is { Length: > 0 } resourceName
            && scene.RendererContext.FileLoader.LoadFileCompiled(resourceName)?.DataBlock is PostProcessing postProcessing)
        {
            volume.LoadPostProcessResource(postProcessing);
        }

        scene.PostProcessInfo.AddPostProcessVolume(volume);
    }

    private float GetExposureLimit(string key, string logKey)
    {
        var limit = GetFloatKey(key);

        return limit > 0f ? limit : MathF.Pow(2f, GetFloatKey(logKey));
    }
}
