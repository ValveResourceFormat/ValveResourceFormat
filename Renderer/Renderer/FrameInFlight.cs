using System;
using System.Collections.Generic;
using System.Text;
using ValveResourceFormat.Renderer.RHI;
using Vortice.Vulkan;
namespace ValveResourceFormat.Renderer;

internal class FrameInFlight
{
    public CommandList CommandList { get; private set; }

    internal uint lastSubmissionIndex;

    public VkSemaphore PresentComplete { get; private set; }

    public FrameInFlight()
    {
        CommandList = new(RenderDevice!.QueueFamilyIndices.GraphicsFamily!.Value);

        PresentComplete = RenderDevice!.CreateSemaphore();
    }
}
