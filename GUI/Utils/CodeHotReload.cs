#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandlerAttribute(typeof(GUI.Utils.CodeHotReloadService))]

namespace GUI.Utils;

// Ref: https://learn.microsoft.com/visualstudio/debugger/hot-reload-metadataupdatehandler
public static class CodeHotReloadService
{
    public static event EventHandler? CodeHotReloaded;

#pragma warning disable IDE0060 // Remove unused parameter
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        Log.Debug(nameof(CodeHotReloadService), ".NET code hot reloaded");

        CodeHotReloaded?.Invoke(null, new EventArgs());
    }
}
#endif
