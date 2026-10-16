global using static ValveResourceFormat.Renderer.RHI.S2vDevice;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
//using static SDL.SDL3;
using static Vortice.Vulkan.Vma;
using static Vortice.Vulkan.Vulkan;

namespace ValveResourceFormat.Renderer.RHI
{
    /// <summary>
    /// Owns the single render device, which is created once in the background and never retried.
    /// </summary>
    public static class S2vDevice
    {
        private static Task? creationTask;

        /// <summary>
        /// Gets the render device, or null if it has not been created yet or creation failed.
        /// </summary>
        public static Device? RenderDevice { get; private set; }

        /// <summary>
        /// Gets the reason the render device could not be created, if it failed.
        /// </summary>
        public static Exception? RenderDeviceError { get; private set; }

        /// <summary>
        /// Starts creating the render device on a background thread. Subsequent calls do nothing.
        /// </summary>
        public static void BeginCreateRenderDevice()
        {
            creationTask ??= Task.Run(static () =>
            {
                try
                {
                    RenderDevice = new Device();
                }
                catch (Exception e)
                {
                    RenderDeviceError = e;
                }
            });
        }

        /// <summary>
        /// Blocks until device creation has finished.
        /// </summary>
        /// <returns>The render device, or null if creation failed; see <see cref="RenderDeviceError"/>.</returns>
        public static Device? WaitForRenderDevice()
        {
            BeginCreateRenderDevice();
            creationTask!.Wait();
            return RenderDevice;
        }
    }

    public struct QueueFamilyIndices
    {
        public uint? GraphicsFamily;
        public uint? PresentFamily;
        public uint? TransferFamily;

        public readonly bool IsComplete =>
            GraphicsFamily.HasValue && PresentFamily.HasValue && TransferFamily.HasValue;
    };

    enum BindlessBindingIndex
    {
        Sampler = 0,
        CombinedImageSampler = 1,
        SampledImage = 2,
        UniformBuffer = 6,
        StorageBuffer = 7,
    }

    public class Device
    {

        public VkInstanceApi VkInstanceApi;
        public VkDeviceApi VkDeviceApi;
        public VkPhysicalDevice VkPhysicalDevice;

        private class BindlessManagerType
        {
            private sealed class BindlessSlotAllocator
            {
                private readonly Stack<uint> FreeIndices = new();
                private uint NextIndex = 0;

                private uint Capacity;

                public BindlessSlotAllocator(uint capacity)
                {
                    Capacity = capacity;
                }

                public uint Allocate()
                {
                    if (FreeIndices.TryPop(out uint index))
                    {
                        return index;
                    }

                    if (NextIndex == Capacity)
                    {
                        throw new InvalidOperationException("Ran out of bindless slots for the descriptor type");
                    }

                    index = NextIndex++;
                    return index;
                }

                public void Free(uint index)
                {
                    FreeIndices.Push(index);
                }
            }
            private readonly Dictionary<BindlessBindingIndex, BindlessSlotAllocator> Allocators = [];

            public BindlessManagerType(IReadOnlyDictionary<BindlessBindingIndex, uint> capacities)
            {
                foreach (var (binding, capacity) in capacities)
                {
                    Allocators[binding] = new BindlessSlotAllocator(capacity);
                }
            }

            public uint GetBindlessIndex(BindlessBindingIndex bindingIndex) => Allocators[bindingIndex].Allocate();

            public void FreeBindlessIndex(BindlessBindingIndex bindingIndex, uint index) => Allocators[bindingIndex].Free(index);
        }

        // Slots and samplers are also created on loading and thread pool threads while frames are recorded,
        // so the slot allocators, writes into the shared set and the sampler cache must not overlap
        private readonly Lock BindlessLock = new();
        private readonly Lock SamplerLock = new();

        // Uploads are submitted from thread pool threads, and a queue must not be submitted to from two threads
        // at once. Without a dedicated transfer family both locks are the same object, since the transfer queue
        // is then the graphics queue, which the render thread submits and presents on.
        internal Lock GraphicsQueueLock { get; } = new();
        internal Lock TransferQueueLock { get; private set; } = null!;

        private BindlessManagerType BindlessManager = null!;
        private readonly Dictionary<BindlessBindingIndex, uint> BindlessCapacities = [];

        /// <summary>The core features the device was created with.</summary>
        public VkPhysicalDeviceFeatures EnabledFeatures { get; private set; }

        /// <summary>The highest anisotropy a sampler may use, only meaningful when <c>samplerAnisotropy</c> is enabled.</summary>
        public float MaxSamplerAnisotropy { get; private set; }

        /// <summary>The GPU name and driver version, for bug reports.</summary>
        public string GpuRendererAndDriver { get; private set; } = string.Empty;

        public VkDescriptorSetLayout SharedBindlessDescriptorSetLayout;
        public VkDescriptorSet SharedBindlessDescriptorSet;
        public VkPipelineLayout SharedPipelineLayout;
        public VkDescriptorPool SharedDescriptorPool;

        public unsafe class SamplerCreateInfoComparer : IEqualityComparer<VkSamplerCreateInfo>
        {
            public bool Equals(VkSamplerCreateInfo x, VkSamplerCreateInfo y)
            {
                return x.magFilter == y.magFilter
                    && x.minFilter == y.minFilter
                    && x.mipmapMode == y.mipmapMode
                    && x.addressModeU == y.addressModeU
                    && x.addressModeV == y.addressModeV
                    && x.addressModeW == y.addressModeW
                    && x.mipLodBias == y.mipLodBias
                    && x.anisotropyEnable == y.anisotropyEnable
                    && x.maxAnisotropy == y.maxAnisotropy
                    && x.compareEnable == y.compareEnable
                    && x.compareOp == y.compareOp
                    && x.minLod == y.minLod
                    && x.maxLod == y.maxLod
                    && x.borderColor == y.borderColor
                    && x.unnormalizedCoordinates == y.unnormalizedCoordinates;
                // deliberately ignoring sType and pNext
            }

            public int GetHashCode(VkSamplerCreateInfo info)
            {
                var hash = new HashCode();
                hash.Add(info.magFilter);
                hash.Add(info.minFilter);
                hash.Add(info.mipmapMode);
                hash.Add(info.addressModeU);
                hash.Add(info.addressModeV);
                hash.Add(info.addressModeW);
                hash.Add(info.mipLodBias);
                hash.Add(info.anisotropyEnable);
                hash.Add(info.maxAnisotropy);
                hash.Add(info.compareEnable);
                hash.Add(info.compareOp);
                hash.Add(info.minLod);
                hash.Add(info.maxLod);
                hash.Add(info.borderColor);
                hash.Add(info.unnormalizedCoordinates);
                return hash.ToHashCode();
            }
        }
        public Dictionary<VkSamplerCreateInfo, VkSampler> Samplers = new Dictionary<VkSamplerCreateInfo, VkSampler>(new SamplerCreateInfoComparer());
        private readonly Dictionary<VkSamplerCreateInfo, uint> SamplerSlots = new(new SamplerCreateInfoComparer());

        public QueueFamilyIndices QueueFamilyIndices;
        private VkQueue _graphicsQueue;
        public VkQueue GraphicsQueue => _graphicsQueue;
        private VkQueue _transferQueue;
        public VkQueue TransferQueue => _transferQueue;
        public VmaAllocator VmaAllocator;

        [UnmanagedCallersOnly]
        unsafe private static uint DebugCallback(
    VkDebugUtilsMessageSeverityFlagsEXT messageSeverity,
    VkDebugUtilsMessageTypeFlagsEXT messageType,
    VkDebugUtilsMessengerCallbackDataEXT* callbackData,
    void* userData)
        {
            System.Diagnostics.Debug.WriteLine(
                Marshal.PtrToStringAnsi((nint)callbackData->pMessage));

            return 0;
        }

        unsafe public Device()
        {
            vkInitialize();

            VkUtf8String appName = "S2v"u8;
            VkUtf8String engineName = "S2vEngine"u8;

            var appInfo = new VkApplicationInfo
            {
                pApplicationName = appName,
                applicationVersion = new VkVersion(1, 0, 0),
                pEngineName = engineName,
                engineVersion = new VkVersion(20, 0, 0),
                apiVersion = VkVersion.Version_1_3
            };

            VkDebugUtilsMessengerCreateInfoEXT debugCreateInfo = new()
            {
                messageSeverity =
        VkDebugUtilsMessageSeverityFlagsEXT.Verbose |
        VkDebugUtilsMessageSeverityFlagsEXT.Info |
        VkDebugUtilsMessageSeverityFlagsEXT.Warning |
        VkDebugUtilsMessageSeverityFlagsEXT.Error,

                messageType =
        VkDebugUtilsMessageTypeFlagsEXT.General |
        VkDebugUtilsMessageTypeFlagsEXT.Validation |
        VkDebugUtilsMessageTypeFlagsEXT.Performance,

                pfnUserCallback = &DebugCallback
            };

            var layerNamesPtr = (byte*)Marshal.StringToHGlobalAnsi("VK_LAYER_KHRONOS_validation");

            string[] layerNames = new string[] { "VK_LAYER_KHRONOS_validation" };

            VkStringArray VkLayerNames = new VkStringArray(layerNames);

            //uint sdlExtensionCount = 0;
            //byte** sdlExtensions = SDL_Vulkan_GetInstanceExtensions(&sdlExtensionCount);

            //// Combine SDL's required extensions with VK_EXT_debug_utils
            //uint totalExtensionCount = sdlExtensionCount + 1;
            //byte** allExtensions = stackalloc byte*[(int)totalExtensionCount];

            //for (uint i = 0; i < sdlExtensionCount; i++)
            //{
            //    allExtensions[i] = sdlExtensions[i];
            //}

            //byte* debugUtilsExtName = (byte*)Marshal.StringToHGlobalAnsi("VK_EXT_debug_utils");
            //allExtensions[sdlExtensionCount] = debugUtilsExtName;

            string[] instanceExtensions = new string[] { "VK_EXT_debug_utils", "VK_KHR_win32_surface", "VK_KHR_surface" };

            VkStringArray instanceExtensionsArray = new VkStringArray(instanceExtensions);

            var instanceCreateInfo = new VkInstanceCreateInfo
            {
                pNext = &debugCreateInfo,
                enabledLayerCount = VkLayerNames.Length,
                ppEnabledLayerNames = VkLayerNames,
                enabledExtensionCount = (uint)instanceExtensions.Length,
                ppEnabledExtensionNames = instanceExtensionsArray,
                pApplicationInfo = &appInfo
            };

            if (vkCreateInstance(&instanceCreateInfo, null, out VkInstance instance) != VkResult.Success)
                throw new Exception("failed to create instance");

            VkInstanceApi = new VkInstanceApi(instance);

            VkDebugUtilsMessengerEXT debugMessenger;
            VkInstanceApi.vkCreateDebugUtilsMessengerEXT(&debugCreateInfo, null, &debugMessenger).CheckResult();

            uint deviceCount = 0;

            VkInstanceApi.vkEnumeratePhysicalDevices(&deviceCount, null);

            if (deviceCount == 0) throw new Exception("no GPUs with SilkVk support");

            var devices = new VkPhysicalDevice[deviceCount];
            fixed (VkPhysicalDevice* devicesPtr = devices)
            {
                VkInstanceApi.vkEnumeratePhysicalDevices(&deviceCount, devicesPtr);
            }

            //just take the zeroth device for now!
            VkPhysicalDevice = devices[0];

            VkInstanceApi.vkGetPhysicalDeviceProperties(VkPhysicalDevice, out var properties);

            MaxSamplerAnisotropy = properties.limits.maxSamplerAnisotropy;

            var properties12 = new VkPhysicalDeviceVulkan12Properties();
            var properties2 = new VkPhysicalDeviceProperties2 { pNext = &properties12 };
            VkInstanceApi.vkGetPhysicalDeviceProperties2(VkPhysicalDevice, &properties2);

            var gpuName = Marshal.PtrToStringUTF8((nint)properties.deviceName);
            var driverName = Marshal.PtrToStringUTF8((nint)properties12.driverName);
            var driverInfo = Marshal.PtrToStringUTF8((nint)properties12.driverInfo);
            var apiVersion = new VkVersion(properties.apiVersion);
            GpuRendererAndDriver = $"GPU: {gpuName}, Driver: {driverName} {driverInfo}, Vulkan {apiVersion.Major}.{apiVersion.Minor}.{apiVersion.Patch}";

            Console.WriteLine(GpuRendererAndDriver);

            ComputeBindlessCapacities(properties12);
            BindlessManager = new BindlessManagerType(BindlessCapacities);

            VkInstanceApi.vkGetPhysicalDeviceFeatures(VkPhysicalDevice, out var supportedFeatures);

            var features12 = new VkPhysicalDeviceVulkan12Features
            {
                descriptorBindingSampledImageUpdateAfterBind = true,
                descriptorBindingUniformBufferUpdateAfterBind = true,
                descriptorBindingStorageBufferUpdateAfterBind = true,
                descriptorBindingUpdateUnusedWhilePending = true,
                descriptorBindingPartiallyBound = true,
                descriptorBindingVariableDescriptorCount = true,

                runtimeDescriptorArray = true
            };

            var features13 = new VkPhysicalDeviceVulkan13Features
            {
                pNext = &features12,
                dynamicRendering = true,
                synchronization2 = true
            };

            QueueFamilyIndices = FindQueueFamilies();

            var graphicsQueuePriority = 1.0f;
            var graphicsQueueCreateInfo = new VkDeviceQueueCreateInfo
            {
                sType = VkStructureType.DeviceQueueCreateInfo,
                queueFamilyIndex = QueueFamilyIndices.GraphicsFamily!.Value,
                queueCount = 1,
                pQueuePriorities = &graphicsQueuePriority
            };

            var transferQueuePriority = 0.1f;

            var transferQueueCreateInfo = new VkDeviceQueueCreateInfo
            {
                sType = VkStructureType.DeviceQueueCreateInfo,
                queueFamilyIndex = QueueFamilyIndices.TransferFamily!.Value,
                queueCount = 1,
                pQueuePriorities = &transferQueuePriority
            };

            // Each family may only be listed once, without a dedicated transfer family the graphics queue does both
            var hasDedicatedTransferFamily = QueueFamilyIndices.TransferFamily != QueueFamilyIndices.GraphicsFamily;
            var queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[2] { graphicsQueueCreateInfo, transferQueueCreateInfo };

            string[] extensions = new string[] { "VK_KHR_swapchain", "VK_EXT_image_view_min_lod" };

            VkStringArray extensionsArray = new VkStringArray(extensions);

            // Compressed formats can only be used with their feature enabled, the rest is optional quality
            var features10 = new VkPhysicalDeviceFeatures
            {
                imageCubeArray = true,
                samplerAnisotropy = supportedFeatures.samplerAnisotropy,
                textureCompressionBC = supportedFeatures.textureCompressionBC,
                textureCompressionETC2 = supportedFeatures.textureCompressionETC2,
            };

            EnabledFeatures = features10;

            var deviceCreateInfo = new VkDeviceCreateInfo
            {
                sType = VkStructureType.DeviceCreateInfo,
                queueCreateInfoCount = hasDedicatedTransferFamily ? 2u : 1u,
                pQueueCreateInfos = queueCreateInfos,

                pEnabledFeatures = &features10,
                enabledExtensionCount = extensionsArray.Length,
                ppEnabledExtensionNames = extensionsArray,
                pNext = &features13
            };
            VkDevice createdDevice;
            var result = VkInstanceApi.vkCreateDevice(VkPhysicalDevice, &deviceCreateInfo, null, &createdDevice);

            if (result != VkResult.Success)
                throw new Exception($"failed to create logical device: {result}");

            VkDeviceApi = new VkDeviceApi(VkInstanceApi, createdDevice);

            VkDeviceApi.vkGetDeviceQueue(QueueFamilyIndices.GraphicsFamily!.Value, 0, out _graphicsQueue);

            VkDeviceApi.vkGetDeviceQueue(QueueFamilyIndices.TransferFamily!.Value, 0, out _transferQueue);

            TransferQueueLock = hasDedicatedTransferFamily ? new Lock() : GraphicsQueueLock;

            VmaAllocatorCreateInfo allocatorCreateInfo = new()
            {
                instance = VkInstanceApi.Instance,
                device = VkDeviceApi.Device,
                physicalDevice = VkPhysicalDevice,
                vulkanApiVersion = appInfo.apiVersion,
            };

            vmaCreateAllocator(&allocatorCreateInfo, out var allocator);
            VmaAllocator = allocator;

            CreateSharedBindlessDescriptorSetLayout();
            CreateSharedPipelineLayout();
            CreateSharedDescriptorPool();
            CreateSharedBindlessDescriptorSet();

            // The first sampler lands in slot 0, which handles without a sampler of their own index
            GetSampler(Texture.DefaultSamplerInfo);
        }
        unsafe QueueFamilyIndices FindQueueFamilies()
        {
            var indices = new QueueFamilyIndices();

            uint count = 0;
            VkInstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice, &count, null);

            var families = new VkQueueFamilyProperties[count];
            fixed (VkQueueFamilyProperties* famPtr = families)
            {
                VkInstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice, &count, famPtr);
            }

            for (uint i = 0; i < count; i++)
            {
                var flags = families[i].queueFlags;
                //bit of a hack, because present support is technically 
                //VkInstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, i, surface, out var presentSupport);
                if (flags.HasFlag(VkQueueFlags.Graphics))
                {
                    indices.GraphicsFamily = i;
                }

                // prefer a DEDICATED transfer family: has transfer bit, but NOT graphics
                // (graphics/compute queues always implicitly support transfer anyway)
                if (flags.HasFlag(VkQueueFlags.Transfer) &&
                    !flags.HasFlag(VkQueueFlags.Graphics))
                {
                    indices.TransferFamily = i;
                }

                if (indices.IsComplete)
                    break;
            }

            if (!indices.GraphicsFamily.HasValue)
            {
                throw new Exception("This RenderDevice does not have a queue family supporting graphics AND present");
            }

            // fallback: no dedicated transfer family found, just reuse graphics
            // (graphics-capable queues are guaranteed to support transfer per spec)
            if (!indices.TransferFamily.HasValue && indices.GraphicsFamily.HasValue)
            {
                indices.TransferFamily = indices.GraphicsFamily;
            }

            return indices;
        }

        public unsafe void SetObjectDebugName(ulong handle, VkObjectType type, string name)
        {
            //Q: Performance?
            var array = new VkStringArray([name]);
            byte* pName = *(byte**)array;
            VkDebugUtilsObjectNameInfoEXT nameInfo = new()
            {
                objectType = type,
                objectHandle = handle,
                pObjectName = (byte*)pName
            };
            VkInstanceApi.vkSetDebugUtilsObjectNameEXT(VkDeviceApi.Device, &nameInfo);
        }
        public unsafe VkSurfaceKHR CreateSurfaceFromWindowHandle(nint windowHandle)
        {
            [DllImport("user32.dll")]
            static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

            nint hinstance = GetWindowLongPtr(windowHandle, -6);

            VkWin32SurfaceCreateInfoKHR createInfo = new();
            createInfo.hinstance = hinstance; // or the HINSTANCE you created the window with
            createInfo.hwnd = windowHandle;                       // your raw HWND

            VkResult result = VkInstanceApi.vkCreateWin32SurfaceKHR(&createInfo, out var surface);
            return surface;
        }

        public VkSampler CreateSampler(VkSamplerCreateInfo samplerInfo)
        {
            using var _ = SamplerLock.EnterScope();

            if (Samplers.TryGetValue(samplerInfo, out var sampler))
            {
                return sampler;
            }
            VkDeviceApi.vkCreateSampler(samplerInfo, out sampler);
            Samplers.Add(samplerInfo, sampler);
            return sampler;
        }

        /// <summary>Gets the sampler table slot for a sampler state, creating the sampler on first use.</summary>
        /// <param name="samplerInfo">The sampler state.</param>
        /// <returns>The index into the shader's sampler table.</returns>
        public unsafe uint GetSampler(VkSamplerCreateInfo samplerInfo)
        {
            var sampler = CreateSampler(samplerInfo);

            using var _ = BindlessLock.EnterScope();

            if (SamplerSlots.TryGetValue(samplerInfo, out var bindlessIndex))
            {
                return bindlessIndex;
            }

            bindlessIndex = BindlessManager.GetBindlessIndex(BindlessBindingIndex.Sampler);

            VkDescriptorImageInfo imageInfo = new()
            {
                sampler = sampler,
            };

            VkWriteDescriptorSet write = new()
            {
                dstSet = SharedBindlessDescriptorSet,
                dstBinding = (uint)BindlessBindingIndex.Sampler,
                dstArrayElement = bindlessIndex,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.Sampler,
                pImageInfo = &imageInfo
            };

            VkDeviceApi.vkUpdateDescriptorSets(1, &write, 0, null);

            SamplerSlots.Add(samplerInfo, bindlessIndex);
            return bindlessIndex;
        }

        public unsafe VkSemaphore CreateSemaphore(bool isTimeline = false)
        {
            VkSemaphoreTypeCreateInfo typeCreateInfo = new()
            {
                semaphoreType = isTimeline ? VkSemaphoreType.Timeline : VkSemaphoreType.Binary
            };

            VkSemaphoreCreateInfo createInfo = new()
            {
                pNext = &typeCreateInfo
            };

            VkDeviceApi.vkCreateSemaphore(createInfo, out var semaphore);
            return semaphore;
        }

        public unsafe void WaitOnTimelineSemaphore(VkSemaphore semaphore, ulong timelineValue)
        {
            VkSemaphoreWaitInfo waitInfo = new()
            {
                semaphoreCount = 1,
                pSemaphores = &semaphore,
                pValues = &timelineValue
            };

            VkDeviceApi.vkWaitSemaphores(&waitInfo, ulong.MaxValue);
        }

        public VkFence CreateFence(bool isSignaled = false)
        {
            VkFenceCreateInfo fenceCreateInfo = new() { flags = (VkFenceCreateFlags)Convert.ToInt32(isSignaled) };

            VkDeviceApi.vkCreateFence(fenceCreateInfo, out var fence);
            return fence;
        }

        public void WaitDeviceIdle()
        {
            VkDeviceApi.vkDeviceWaitIdle();
        }
        public void WaitForFences(VkFence fence, bool waitForAll = true, ulong timeout = ulong.MaxValue)
        {
            VkDeviceApi.vkWaitForFences(fence, waitForAll, timeout);
        }

        public void WaitForFences(Span<VkFence> fence, bool waitForAll = true, ulong timeout = ulong.MaxValue)
        {
            VkDeviceApi.vkWaitForFences(fence, waitForAll, timeout);
        }

        public void ResetFences(VkFence fence)
        {
            var result = VkDeviceApi.vkResetFences(fence);
        }

        public void ResetFences(Span<VkFence> fences)
        {
            VkDeviceApi.vkResetFences(fences);
        }

        unsafe public void SubmitGraphics(CommandList list, VkSemaphore imageAvailableSemaphore = new VkSemaphore(), VkSemaphore renderFinishedSemaphore = new VkSemaphore(), VkFence fifFreed = new VkFence())
        {
            VkCommandBufferSubmitInfo cmdBufferSubmitInfo = new VkCommandBufferSubmitInfo
            {
                commandBuffer = list.Handle
            };

            VkSemaphoreSubmitInfo imgAvailableSemaphoreInfo = new VkSemaphoreSubmitInfo
            {
                semaphore = imageAvailableSemaphore,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput
            };

            VkSemaphoreSubmitInfo renderFinishedSemaphoreInfo = new VkSemaphoreSubmitInfo
            {
                semaphore = renderFinishedSemaphore,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput
            };

            VkSubmitInfo2 submitInfo = new VkSubmitInfo2
            {
                waitSemaphoreInfoCount = (uint)Convert.ToInt32(!imageAvailableSemaphore.IsNull),
                pWaitSemaphoreInfos = &imgAvailableSemaphoreInfo,
                commandBufferInfoCount = 1,
                pCommandBufferInfos = &cmdBufferSubmitInfo,
                signalSemaphoreInfoCount = (uint)Convert.ToInt32(!renderFinishedSemaphore.IsNull),
                pSignalSemaphoreInfos = &renderFinishedSemaphoreInfo
            };

            using (GraphicsQueueLock.EnterScope())
            {
                VkDeviceApi.vkQueueSubmit2(_graphicsQueue, submitInfo, fifFreed);
            }
        }
        unsafe public void SubmitGraphics(CommandList list, VkSemaphore imageAvailableSemaphore, VkSemaphore renderFinishedSemaphore, VkSemaphore timelineSemaphore, ulong timelineSignalValue)
        {
            VkCommandBufferSubmitInfo cmdBufferSubmitInfo = new VkCommandBufferSubmitInfo
            {
                commandBuffer = list.Handle
            };

            VkSemaphoreSubmitInfo imgAvailableSemaphoreInfo = new VkSemaphoreSubmitInfo
            {
                semaphore = imageAvailableSemaphore,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput
            };

            VkSemaphoreSubmitInfo renderFinishedSemaphoreInfo = new VkSemaphoreSubmitInfo
            {
                semaphore = renderFinishedSemaphore,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput
            };

            VkSemaphoreSubmitInfo timelineSemaphoreInfo = new VkSemaphoreSubmitInfo
            {
                semaphore = timelineSemaphore,
                stageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
                value = timelineSignalValue
            };

            VkSemaphoreSubmitInfo[] submitInfos = [renderFinishedSemaphoreInfo, timelineSemaphoreInfo];

            fixed (VkSemaphoreSubmitInfo* pInfos = submitInfos)
            {
                VkSubmitInfo2 submitInfo = new VkSubmitInfo2
                {
                    waitSemaphoreInfoCount = (uint)Convert.ToInt32(!imageAvailableSemaphore.IsNull),
                    pWaitSemaphoreInfos = &imgAvailableSemaphoreInfo,
                    commandBufferInfoCount = 1,
                    pCommandBufferInfos = &cmdBufferSubmitInfo,
                    signalSemaphoreInfoCount = (uint)submitInfos.Length,
                    pSignalSemaphoreInfos = pInfos
                };

                using (GraphicsQueueLock.EnterScope())
                {
                    VkDeviceApi.vkQueueSubmit2(_graphicsQueue, submitInfo, 0);
                }
            }
        }

        unsafe public void SubmitTransfer(CommandList list, VkFence transferFinished)
        {
            VkCommandBufferSubmitInfo cmdBufferSubmitInfo = new VkCommandBufferSubmitInfo
            {
                commandBuffer = list.Handle
            };

            VkSubmitInfo2 submitInfo = new VkSubmitInfo2
            {
                commandBufferInfoCount = 1,
                pCommandBufferInfos = &cmdBufferSubmitInfo,
            };

            using (TransferQueueLock.EnterScope())
            {
                VkDeviceApi.vkQueueSubmit2(_transferQueue, submitInfo, transferFinished);
            }
        }

        /// <summary>Whether uploads run on a separate queue family, which then has to hand images over to the graphics family.</summary>
        public bool HasDedicatedTransferFamily => QueueFamilyIndices.TransferFamily != QueueFamilyIndices.GraphicsFamily;

        /// <summary>
        /// Returns whether images of <paramref name="format"/> with the given shape can be sampled and uploaded to.
        /// Compressed formats in particular depend on device features, and 3D support is optional for most of them.
        /// </summary>
        /// <param name="format">The texel format.</param>
        /// <param name="dimension">The image shape.</param>
        public unsafe bool SupportsSampledImage(VkFormat format, TextureDimension dimension)
        {
            var (imageType, _) = Image.GetImageTypes(dimension);
            var flags = Image.IsCube(dimension) ? VkImageCreateFlags.CubeCompatible : VkImageCreateFlags.None;

            VkImageFormatProperties imageFormatProperties;
            var result = VkInstanceApi.vkGetPhysicalDeviceImageFormatProperties(VkPhysicalDevice, format, imageType, VkImageTiling.Optimal,
                VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst, flags, &imageFormatProperties);

            return result == VkResult.Success;
        }

        public unsafe uint GetBindlessSlot(VkDescriptorType descriptorType, VkBuffer bufferHandle)
        {
            using var _ = BindlessLock.EnterScope();

            var bindlessIndex = BindlessManager.GetBindlessIndex((BindlessBindingIndex)descriptorType);

            VkDescriptorBufferInfo bufferInfo = new()
            {
                buffer = bufferHandle,
                offset = 0,
                range = Vulkan.VK_WHOLE_SIZE
            };

            VkWriteDescriptorSet write = new()
            {
                dstSet = RenderDevice!.SharedBindlessDescriptorSet,
                // Bindings are numbered after their descriptor type, see CreateSharedBindlessDescriptorSetLayout
                dstBinding = (uint)(BindlessBindingIndex)descriptorType,
                dstArrayElement = bindlessIndex,
                descriptorCount = 1,
                descriptorType = descriptorType,
                pBufferInfo = &bufferInfo
            };

            RenderDevice!.VkDeviceApi.vkUpdateDescriptorSets(1, &write, 0, null);

            return bindlessIndex;
        }

        internal unsafe uint GetBindlessSlot(VkDescriptorType descriptorType, VkImageView imageViewHandle, VkSampler samplerHandle)
        {
            using var _ = BindlessLock.EnterScope();

            var bindlessIndex = BindlessManager.GetBindlessIndex((BindlessBindingIndex)descriptorType);

            VkDescriptorImageInfo imageInfo = new();
            imageInfo.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
            imageInfo.imageView = imageViewHandle;
            imageInfo.sampler = samplerHandle;

            // VKTODO: The sampled image slot shares the combined slot's index until textures allocate their own
            VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[2];
            writes[0] = new()
            {
                dstSet = RenderDevice!.SharedBindlessDescriptorSet,
                dstBinding = 1,
                dstArrayElement = bindlessIndex,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                pImageInfo = &imageInfo
            };
            writes[1] = writes[0];
            writes[1].dstBinding = (uint)BindlessBindingIndex.SampledImage;
            writes[1].descriptorType = VkDescriptorType.SampledImage;

            RenderDevice!.VkDeviceApi.vkUpdateDescriptorSets(2, writes, 0, null);

            return bindlessIndex;
        }

        // Rewrites a slot that frames still in flight may be sampling through. The spec only allows updating
        // descriptors that pending command buffers do not use (UPDATE_UNUSED_WHILE_PENDING), and promises nothing
        // about what in-flight work reads from one that is rewritten under it. This relies on drivers handing it
        // either the old or the new descriptor, which has to hold on every vendor. If it does not, the fallback
        // is a fresh slot per change, with the old one freed once the owning renderer's frames are done with it.
        internal unsafe void UpdateBindlessCombinedSampler(VkImageView imageViewHandle, VkSampler samplerHandle, uint bindlessIndex)
        {
            using var _ = BindlessLock.EnterScope();

            VkDescriptorImageInfo imageInfo = new();
            imageInfo.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
            imageInfo.imageView = imageViewHandle;
            imageInfo.sampler = samplerHandle;

            VkWriteDescriptorSet write = new()
            {
                dstSet = RenderDevice!.SharedBindlessDescriptorSet,
                dstBinding = 1,
                dstArrayElement = bindlessIndex,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                pImageInfo = &imageInfo
            };

            RenderDevice!.VkDeviceApi.vkUpdateDescriptorSets(1, &write, 0, null);
        }

        internal void FreeBindlessSlot(VkDescriptorType descriptorType, uint index)
        {
            using var _ = BindlessLock.EnterScope();

            BindlessManager.FreeBindlessIndex((BindlessBindingIndex)descriptorType, index);
        }

        // Combined image samplers count against both the sampler and the sampled image limits, and every
        // binding is visible to all stages, so the per stage limits apply on top of the per set ones
        private void ComputeBindlessCapacities(in VkPhysicalDeviceVulkan12Properties limits)
        {
            const uint DesiredCapacity = 65536;
            const uint SamplerCapacity = 1024;

            BindlessCapacities[BindlessBindingIndex.CombinedImageSampler] = Math.Min(DesiredCapacity, Math.Min(
                Math.Min(limits.maxDescriptorSetUpdateAfterBindSampledImages, limits.maxPerStageDescriptorUpdateAfterBindSampledImages),
                Math.Min(limits.maxDescriptorSetUpdateAfterBindSamplers, limits.maxPerStageDescriptorUpdateAfterBindSamplers)));

            BindlessCapacities[BindlessBindingIndex.SampledImage] = BindlessCapacities[BindlessBindingIndex.CombinedImageSampler];

            BindlessCapacities[BindlessBindingIndex.Sampler] = Math.Min(SamplerCapacity,
                Math.Min(limits.maxDescriptorSetUpdateAfterBindSamplers, limits.maxPerStageDescriptorUpdateAfterBindSamplers));

            BindlessCapacities[BindlessBindingIndex.UniformBuffer] = Math.Min(DesiredCapacity,
                Math.Min(limits.maxDescriptorSetUpdateAfterBindUniformBuffers, limits.maxPerStageDescriptorUpdateAfterBindUniformBuffers));

            BindlessCapacities[BindlessBindingIndex.StorageBuffer] = Math.Min(DesiredCapacity,
                Math.Min(limits.maxDescriptorSetUpdateAfterBindStorageBuffers, limits.maxPerStageDescriptorUpdateAfterBindStorageBuffers));

            // All bindings together must also fit the per stage resource limit
            var total = 0UL;

            foreach (var capacity in BindlessCapacities.Values)
            {
                total += capacity;
            }

            if (total > limits.maxPerStageUpdateAfterBindResources)
            {
                var scale = (double)limits.maxPerStageUpdateAfterBindResources / total;

                foreach (var binding in BindlessCapacities.Keys.ToArray())
                {
                    BindlessCapacities[binding] = (uint)(BindlessCapacities[binding] * scale);
                }
            }
        }

        private unsafe void CreateSharedBindlessDescriptorSetLayout()
        {

            var bindings = Enum.GetValues<BindlessBindingIndex>();

            var descriptorSetLayoutBindings = new VkDescriptorSetLayoutBinding[bindings.Length];

            for (var i = 0; i < bindings.Length; i++)
            {
                descriptorSetLayoutBindings[i].binding = (uint)bindings[i];
                //Looks like nonsense, but the bindings match VkDescriptorType's values. I believe this is by design on slangs side.
                descriptorSetLayoutBindings[i].descriptorType = (VkDescriptorType)bindings[i];
                descriptorSetLayoutBindings[i].stageFlags = VkShaderStageFlags.All;
                descriptorSetLayoutBindings[i].descriptorCount = BindlessCapacities[bindings[i]];
            }

            // Slots are written while frames that use other slots of the same binding are still in flight,
            // which only UpdateUnusedWhilePending allows
            VkDescriptorBindingFlags[] bindingFlags = Enumerable.Repeat(
                VkDescriptorBindingFlags.PartiallyBound | VkDescriptorBindingFlags.UpdateAfterBind | VkDescriptorBindingFlags.UpdateUnusedWhilePending,
                bindings.Length).ToArray();

            fixed (VkDescriptorBindingFlags* pBindingFlags = bindingFlags)
            fixed (VkDescriptorSetLayoutBinding* pDescriptorSetLayoutBindings = descriptorSetLayoutBindings)
            {
                VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo = new()
                {
                    bindingCount = (uint)bindings.Length,
                    pBindingFlags = pBindingFlags
                };

                VkDescriptorSetLayoutCreateInfo setLayoutCreateInfo = new()
                {
                    flags = VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool,
                    pNext = &bindingFlagsInfo,
                    bindingCount = (uint)bindings.Length,
                    pBindings = pDescriptorSetLayoutBindings,
                };

                VkDeviceApi.vkCreateDescriptorSetLayout(setLayoutCreateInfo, out SharedBindlessDescriptorSetLayout);
            }
        }

        private unsafe void CreateSharedFixedDescriptorSetLayout()
        { }

        private unsafe void CreateSharedPipelineLayout()
        {

            //PLACEHOLDER
            VkDeviceApi.vkCreateDescriptorSetLayout(new VkDescriptorSetLayoutCreateInfo(), out var setLayout0);

            var sets = new VkDescriptorSetLayout[2]
                    {
                        setLayout0,
                        SharedBindlessDescriptorSetLayout,
                    };

            //Fixed push constant range for us. We push DescriptorHandles and nothing more.
            VkPushConstantRange pushRange = new()
            {
                stageFlags = VkShaderStageFlags.All,
                size = CommandList.PushConstantSize
            };

            fixed (VkDescriptorSetLayout* pSets = sets)
            {
                VkPipelineLayoutCreateInfo layoutCreateInfo = new()
                {
                    setLayoutCount = 2,
                    pSetLayouts = pSets,
                    pushConstantRangeCount = 1,
                    pPushConstantRanges = &pushRange
                };
                VkDeviceApi.vkCreatePipelineLayout(layoutCreateInfo, out SharedPipelineLayout);
            }
        }

        private unsafe void CreateSharedDescriptorPool()
        {
            var bindings = Enum.GetValues<BindlessBindingIndex>();

            VkDescriptorPoolSize[] poolSizes = new VkDescriptorPoolSize[bindings.Length];

            for (var i = 0; i < bindings.Length; i++)
            {
                poolSizes[i].type = (VkDescriptorType)bindings[i];
                poolSizes[i].descriptorCount = BindlessCapacities[bindings[i]];
            }

            fixed (VkDescriptorPoolSize* pPoolSizes = poolSizes)
            fixed (VkDescriptorPool* pPool = &SharedDescriptorPool)
            {
                VkDescriptorPoolCreateInfo poolInfo = new()
                {
                    flags = VkDescriptorPoolCreateFlags.UpdateAfterBind,
                    //Change this if it turns out we want more (like for having the fixed set per render context and FiF)
                    maxSets = 2,
                    poolSizeCount = (uint)bindings.Length,
                    pPoolSizes = pPoolSizes
                };

                VkDeviceApi.vkCreateDescriptorPool(&poolInfo, null, pPool).CheckResult();
            }
        }

        private unsafe void CreateSharedBindlessDescriptorSet()
        {

            fixed (VkDescriptorSetLayout* pSet = &SharedBindlessDescriptorSetLayout)
            {
                VkDescriptorSetAllocateInfo allocInfo = new()
                {
                    descriptorPool = SharedDescriptorPool,
                    descriptorSetCount = 1,
                    pSetLayouts = pSet
                };

                VkDeviceApi.vkAllocateDescriptorSets(allocInfo, out SharedBindlessDescriptorSet).CheckResult();
            }
        }
    }
}
