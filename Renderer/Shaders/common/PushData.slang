module PushData;

__exported import ViewConstants;
__exported import SceneConstants;

public interface IParameters {};

public struct NoParams : IParameters;

public struct PushData<T> where T : IParameters
{
    DescriptorHandle<ConstantBuffer<ViewConstantsData>> viewConstants;
    DescriptorHandle<ConstantBuffer<T>> parameterBuffer;
    DescriptorHandle<ConstantBuffer<SceneBuffers>> sceneBuffers;

    public property ViewConstantsData ViewConstants
    {
        get {
            return *viewConstants;
        }
    }
    public property T Parameters
    {
        get {
            return *parameterBuffer;
        }
    }
    // The buffer rather than its contents, so reading one light does not load all of them
    public property ConstantBuffer<LightingConstantsData> Lighting
    {
        get {
            return *(*sceneBuffers).lighting;
        }
    }
}
