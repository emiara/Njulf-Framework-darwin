// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Core.Native;
using Vma;
using System.Runtime.InteropServices;
using Silk.NET.GLFW;

namespace Njulf_Framework.Rendering.Core;

public unsafe class VulkanContext : IDisposable
{
    private Vk _vk = null!;
    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _transferQueue;
    private uint _graphicsQueueFamily;
    private uint _transferQueueFamily;
    private DebugUtilsMessengerEXT _debugMessenger;
    private Allocator* _vmaAllocator;

    public Instance Instance => _instance;
    public Device Device => _device;
    public PhysicalDevice PhysicalDevice => _physicalDevice;
    public Queue GraphicsQueue => _graphicsQueue;
    public Queue TransferQueue => _transferQueue;
    public uint GraphicsQueueFamily => _graphicsQueueFamily;
    public uint TransferQueueFamily => _transferQueueFamily;
    public Vk VulkanApi => _vk;
    public Allocator* VmaAllocator => _vmaAllocator;

    public VulkanContext(bool enableValidationLayers = true)
    {
        _vk = Vk.GetApi();
        CreateInstance(enableValidationLayers);
        SelectPhysicalDevice();
        CreateLogicalDevice(enableValidationLayers);
        CreateVmaAllocator();
    }

    private void CreateInstance(bool enableValidation)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("YourFramework"),
            ApplicationVersion = new Silk.NET.Core.Version32(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("YourFramework"),
            EngineVersion = new Silk.NET.Core.Version32(1, 0, 0),
            ApiVersion = Vk.Version13
        };

        var extensions = GetRequiredExtensions(enableValidation);
        var layers = enableValidation ? GetValidationLayers() : Array.Empty<string>();

        Console.WriteLine($"Requesting {extensions.Length} extensions:");
        foreach (var ext in extensions)
        {
            Console.WriteLine($"  - {ext}");
        }

        var enabledExtensionNames = new List<IntPtr>();
        foreach (var ext in extensions)
        {
            enabledExtensionNames.Add(SilkMarshal.StringToPtr(ext));
        }

        var enabledLayerNames = new List<IntPtr>();
        foreach (var layer in layers)
        {
            enabledLayerNames.Add(SilkMarshal.StringToPtr(layer));
        }

        var extensionArray = enabledExtensionNames.ToArray();
        var layerArray = enabledLayerNames.ToArray();

        fixed (IntPtr* extensionPtr = extensionArray)
        fixed (IntPtr* layerPtr = layerArray)
        {
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)enabledExtensionNames.Count,
                PpEnabledExtensionNames = (byte**)extensionPtr,
                EnabledLayerCount = (uint)enabledLayerNames.Count,
                PpEnabledLayerNames = (byte**)layerPtr
            };

            if (_vk.CreateInstance(&createInfo, null, out _instance) != Result.Success)
            {
                throw new Exception("Failed to create Vulkan instance. Check that Vulkan SDK is installed and GPU drivers are up to date.");
            }
        }

        // Reload Vk with the instance - IMPORTANT for extensions to work
        if (!_vk.TryGetInstanceExtension(_instance, out KhrSurface khrSurface))
        {
            throw new Exception("Failed to load KHR_surface extension");
        }

        if (!_vk.TryGetInstanceExtension(_instance, out KhrWin32Surface khrWin32Surface) &&
            !_vk.TryGetInstanceExtension(_instance, out KhrXcbSurface khrXcbSurface) &&
            !_vk.TryGetInstanceExtension(_instance, out KhrWaylandSurface khrWaylandSurface))
        {
            Console.WriteLine("Warning: Could not load platform-specific surface extension");
        }

        // Cleanup temp allocations
        foreach (var ptr in enabledExtensionNames)
            Marshal.FreeHGlobal(ptr);
        foreach (var ptr in enabledLayerNames)
            Marshal.FreeHGlobal(ptr);

        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
    }

    private void SelectPhysicalDevice()
    {
        uint deviceCount = 0;
        var result = _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);
        
        if (result != Result.Success)
        {
            throw new Exception($"Failed to enumerate physical devices: {result}");
        }

        if (deviceCount == 0)
        {
            throw new Exception("No Vulkan-capable GPUs found");
        }

        Console.WriteLine($"Found {deviceCount} physical device(s)");

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            result = _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devicesPtr);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to get physical device list: {result}");
            }
        }

        _physicalDevice = devices[0]; // For now, select first device

        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
        var gpuName = SilkMarshal.PtrToString((nint)properties.DeviceName);
        System.Diagnostics.Debug.WriteLine($"Selected GPU: {gpuName}");
        Console.WriteLine($"Selected GPU: {gpuName}");

        VerifyVulkan13Support();
        VerifyMeshShaderSupport();
    }

    private unsafe void VerifyVulkan13Support()
    {
        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
        
        uint apiVersion = properties.ApiVersion;
        
        if (apiVersion < Vk.Version13)
            throw new Exception("Vulkan version 1.3 is not supported on this device");
        
        Console.WriteLine($"Vulkan version 1.3 is supported on this device");
    }

    private unsafe void VerifyMeshShaderSupport()
    {
        var meshFeatures = new PhysicalDeviceMeshShaderFeaturesEXT
        {
            SType = StructureType.PhysicalDeviceMeshShaderFeaturesExt
        };
        var features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &meshFeatures
        };

        _vk.GetPhysicalDeviceFeatures2(_physicalDevice, &features2);
        if (!meshFeatures.MeshShader)
        {
            throw new Exception("VK_EXT_mesh_shader is not supported on this device");
        }
    }

    private void CreateLogicalDevice(bool enableValidation)
    {
        FindQueueFamilies();

        var queueCreateInfos = new List<DeviceQueueCreateInfo>();
        var uniqueQueueFamilies = new HashSet<uint> { _graphicsQueueFamily, _transferQueueFamily };
        
        var features13 = new PhysicalDeviceVulkan13Features();
        var features2 = new PhysicalDeviceFeatures2 { 
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &features13 
        };
        _vk.GetPhysicalDeviceFeatures2(_physicalDevice, &features2);

        Console.WriteLine($"DynamicRendering supported: {features13.DynamicRendering.Value}");

        float queuePriority = 1.0f;
        foreach (var queueFamily in uniqueQueueFamilies)
        {
            var queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = queueFamily,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
            queueCreateInfos.Add(queueCreateInfo);
        }
        
        var vulkan13Features = new PhysicalDeviceVulkan13Features()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            PNext = null,
            DynamicRendering = true,
            Synchronization2 = true,
            Maintenance4 = true,
            
        };
        
        var rtPipelineFeatures = new PhysicalDeviceRayTracingPipelineFeaturesKHR()
        {
            SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
            PNext = &vulkan13Features,
            RayTracingPipeline = true
        };
        
        var asFeatures = new PhysicalDeviceAccelerationStructureFeaturesKHR
        {
            SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
            PNext = &rtPipelineFeatures,
            AccelerationStructure = true
        };
        
        var robustness2Features = new PhysicalDeviceRobustness2FeaturesEXT
        {
            SType = StructureType.PhysicalDeviceRobustness2FeaturesExt,
            PNext = &asFeatures,
            NullDescriptor = true
        };

        var meshShaderFeatures = new PhysicalDeviceMeshShaderFeaturesEXT
        {
            SType = StructureType.PhysicalDeviceMeshShaderFeaturesExt,
            PNext = &robustness2Features,
            MeshShader = true,
            TaskShader = true
        };

        var vulkan12Features = new PhysicalDeviceVulkan12Features()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &meshShaderFeatures,
            BufferDeviceAddress = true,
            DescriptorIndexing = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingStorageBufferUpdateAfterBind = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingVariableDescriptorCount = true,
            ShaderStorageBufferArrayNonUniformIndexing = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            DescriptorBindingUpdateUnusedWhilePending = true,
            ShaderSampledImageArrayNonUniformIndexing = true
        };
        
        var deviceFeatures = new PhysicalDeviceFeatures();

        var extensions = new[]
        {
            KhrSwapchain.ExtensionName,
            KhrRayTracingPipeline.ExtensionName,
            KhrAccelerationStructure.ExtensionName,
            KhrDeferredHostOperations.ExtensionName,
            KhrSynchronization2.ExtensionName,
            KhrDynamicRendering.ExtensionName,
            ExtMeshShader.ExtensionName,
            
        };
        var extensionPtrs = new List<IntPtr>();
        foreach (var ext in extensions)
        {
            extensionPtrs.Add((IntPtr)SilkMarshal.StringToPtr(ext));
        }
        
        var extensionArray = extensionPtrs.ToArray();
        
        fixed (IntPtr* extensionPtr = extensionArray)
        {
            // Get layer names
            var layerNames = enableValidation ? GetValidationLayers() : Array.Empty<string>();

            // Marshal to unmanaged memory
            var layerNamesPointers = stackalloc byte*[layerNames.Length];
            for (int i = 0; i < layerNames.Length; i++)
            {
                layerNamesPointers[i] = (byte*)Marshal.StringToHGlobalAnsi(layerNames[i]);
            }
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                PNext = &vulkan12Features,
                QueueCreateInfoCount = (uint)queueCreateInfos.Count,
                PQueueCreateInfos =
                    (DeviceQueueCreateInfo*)Marshal.AllocHGlobal(queueCreateInfos.Count * sizeof(DeviceQueueCreateInfo)),
                PEnabledFeatures = &deviceFeatures,
                EnabledExtensionCount = (uint)extensionPtrs.Count,
                PpEnabledExtensionNames = (byte**)extensionPtr,
                EnabledLayerCount = (uint)layerNames.Length,
                PpEnabledLayerNames = layerNamesPointers 
            };

            // Copy queue create infos
            var queueCreateInfoArray = createInfo.PQueueCreateInfos;
            for (int i = 0; i < queueCreateInfos.Count; i++)
            {
                queueCreateInfoArray[i] = queueCreateInfos[i];
            }

            if (_vk.CreateDevice(_physicalDevice, &createInfo, null, out _device) != Result.Success)
            {
                throw new Exception("Failed to create logical device");
            }

            _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
            _vk.GetDeviceQueue(_device, _transferQueueFamily, 0, out _transferQueue);

            Marshal.FreeHGlobal((nint)createInfo.PQueueCreateInfos);
        }
        
        foreach (var ptr in extensionPtrs)
                Marshal.FreeHGlobal(ptr);
    }
    
    private void CreateVmaAllocator()
    {
        var createInfo = new AllocatorCreateInfo
        {
            Instance = _instance,
            PhysicalDevice = _physicalDevice,
            Device = _device,
            VulkanApiVersion = Vk.Version13,
            Flags = AllocatorCreateFlags.BufferDeviceAddressBit
        };

        Allocator* allocator;
        
        Apis.CreateAllocator(&createInfo, &allocator);
        _vmaAllocator = allocator;
    }

    private void FindQueueFamilies()
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, queueFamiliesPtr);
        }

        _graphicsQueueFamily = uint.MaxValue;
        _transferQueueFamily = uint.MaxValue;

        for (uint i = 0; i < queueFamilies.Length; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _graphicsQueueFamily = i;
            }

            if ((queueFamilies[i].QueueFlags & QueueFlags.TransferBit) != 0 && i != _graphicsQueueFamily)
            {
                _transferQueueFamily = i;
            }
        }

        if (_transferQueueFamily == uint.MaxValue)
        {
            _transferQueueFamily = _graphicsQueueFamily;
        }

        if (_graphicsQueueFamily == uint.MaxValue)
        {
            throw new Exception("No suitable graphics queue family found");
        }
    }

    private string[] GetRequiredExtensions(bool enableValidation)
    {
        var extensions = new List<string>
        {
            KhrSurface.ExtensionName,
        };

        // Add platform-specific surface extension
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            extensions.Add(KhrWin32Surface.ExtensionName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try Xcb first (most common)
            extensions.Add(KhrXcbSurface.ExtensionName);
        }

        if (enableValidation)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        return extensions.ToArray();
    }

    private string[] GetValidationLayers()
    {
        return new[] { "VK_LAYER_KHRONOS_validation" };
    }

    public void Dispose()
    {
        if (_vmaAllocator != null)
        {
            Apis.DestroyAllocator(_vmaAllocator);
            _vmaAllocator = null;
        }

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            _vk.DestroyDevice(_device, null);
        }

        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
        }
    }
}
    
